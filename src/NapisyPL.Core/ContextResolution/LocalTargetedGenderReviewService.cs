using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed class LocalTargetedGenderReviewService(
    HttpClient httpClient,
    string baseUrl,
    string model,
    int contextRadius = 3)
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly int _contextRadius = contextRadius >= 0 ? contextRadius : throw new ArgumentOutOfRangeException(nameof(contextRadius));

    public async Task<IReadOnlyList<SubtitleCue>> ReviewAsync(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (source.Count != translated.Count)
            throw new ArgumentException("Source and translated cue counts must match.");
        if (source.Count == 0)
            return translated;

        var candidateIds = GenderReviewCandidateSelector.SelectCandidateIds(translated);
        if (candidateIds.Count == 0)
        {
            status?.Report("Enhanced: nie znaleziono kwestii wymagających korekty rodzaju.");
            return translated;
        }

        var output = translated.ToArray();
        var outputPositionById = output.Select((cue, index) => (cue.Index, index)).ToDictionary(x => x.Index, x => x.index);
        var windows = GenderReviewCandidateSelector.BuildContextWindows(source, candidateIds, _contextRadius);
        var speakerSamples = BuildSpeakerSamples(source, cueSpeakers);

        status?.Report($"Enhanced: {candidateIds.Count} kwestii do sprawdzenia w {windows.Count} krótkich oknach…");

        for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceWindow = windows[windowIndex];
            var translatedWindow = sourceWindow.Select(cue => output[outputPositionById[cue.Index]]).ToArray();
            var allowedIds = sourceWindow.Select(cue => cue.Index).Where(candidateIds.Contains).ToHashSet();
            if (allowedIds.Count == 0)
                continue;

            IReadOnlyDictionary<int, string> changed;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
                {
                    Content = JsonContent.Create(new
                    {
                        model,
                        temperature = 0.0,
                        max_tokens = 800,
                        reasoning_effort = "none",
                        chat_template_kwargs = new { enable_thinking = false },
                        response_format = LlamaJsonSchemas.ReviewResponseFormat,
                        messages = new object[]
                        {
                            new
                            {
                                role = "system",
                                content = "Review only clearly wrong Polish grammatical gender/number in explicitly allowed subtitle IDs. Return changed lines as JSON only."
                            },
                            new
                            {
                                role = "user",
                                content = TargetedGenderReviewProtocol.BuildPrompt(
                                    sourceWindow,
                                    translatedWindow,
                                    allowedIds,
                                    cueSpeakers,
                                    speakerSamples)
                            }
                        }
                    })
                };

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Local targeted review: {(int)response.StatusCode} {body}");

                changed = ParseApiResponse(body);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                status?.Report("Enhanced: korekta Qwen nie powiodła się — zachowuję tłumaczenie bazowe.");
                return output;
            }

            // Scope validation intentionally stays outside the best-effort catch. A malformed
            // model response may be skipped, but a request to alter a non-candidate cue is a
            // protocol invariant violation and must remain a hard failure.
            foreach (var (id, text) in changed)
            {
                if (!allowedIds.Contains(id) || !outputPositionById.TryGetValue(id, out var position))
                    throw new InvalidDataException($"Targeted reviewer attempted to change non-candidate cue {id}.");
                output[position] = output[position] with { Text = text };
            }

            progress?.Report((double)(windowIndex + 1) / windows.Count);
            status?.Report($"Enhanced: korekta kontekstu {windowIndex + 1} / {windows.Count}…");
        }

        return output;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> BuildSpeakerSamples(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers)
    {
        return source
            .Select(cue => (Cue: cue, Speaker: cueSpeakers.TryGetValue(cue.Index, out var speaker) ? speaker : null))
            .Where(item => !string.IsNullOrWhiteSpace(item.Speaker))
            .GroupBy(item => item.Speaker!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .OrderByDescending(item => item.Cue.Text.Length)
                    .Select(item => item.Cue.Text)
                    .Distinct(StringComparer.Ordinal)
                    .Take(5)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyDictionary<int, string> ParseApiResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var choice = document.RootElement.GetProperty("choices")[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString()
                : null;
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Targeted gender review reached its token limit.");

            var content = choice.GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("Targeted gender review returned no content.");
            return GenderReviewProtocol.ParseResponse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Targeted gender review returned invalid API JSON.", ex);
        }
    }
}
