using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed class LocalTargetedGenderReviewService(
    HttpClient httpClient,
    string baseUrl,
    string model,
    int contextRadius = 3,
    IAppLogger? logger = null)
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly int _contextRadius = contextRadius >= 0 ? contextRadius : throw new ArgumentOutOfRangeException(nameof(contextRadius));
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;

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
        var allSpeakerSamples = BuildSpeakerSamples(source, cueSpeakers);

        _logger.Info(
            "review_plan",
            ("model", model),
            ("candidateCount", candidateIds.Count),
            ("windowCount", windows.Count),
            ("speakerCount", allSpeakerSamples.Count));
        status?.Report($"Enhanced: {candidateIds.Count} kwestii do sprawdzenia w {windows.Count} krótkich oknach…");

        for (var windowIndex = 0; windowIndex < windows.Count; windowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceWindow = windows[windowIndex];
            var translatedWindow = sourceWindow.Select(cue => output[outputPositionById[cue.Index]]).ToArray();
            var allowedIds = sourceWindow.Select(cue => cue.Index).Where(candidateIds.Contains).ToHashSet();
            if (allowedIds.Count == 0)
                continue;

            var windowSpeakerIds = sourceWindow
                .Select(cue => cueSpeakers.TryGetValue(cue.Index, out var speaker) ? speaker : null)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .Select(speaker => speaker!)
                .ToHashSet(StringComparer.Ordinal);
            var speakerSamples = allSpeakerSamples
                .Where(pair => windowSpeakerIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var prompt = TargetedGenderReviewProtocol.BuildPrompt(
                sourceWindow,
                translatedWindow,
                allowedIds,
                cueSpeakers,
                speakerSamples);

            var oneBasedWindow = windowIndex + 1;
            _logger.Info(
                "review_window_start",
                ("model", model),
                ("windowIndex", oneBasedWindow),
                ("windowCount", windows.Count),
                ("cueCount", sourceWindow.Count),
                ("candidateCount", allowedIds.Count),
                ("speakerCount", speakerSamples.Count),
                ("promptChars", prompt.Length));
            status?.Report($"Enhanced: Qwen sprawdza okno {oneBasedWindow} / {windows.Count}…");

            var stopwatch = Stopwatch.StartNew();
            int? httpStatus = null;
            string body = string.Empty;
            ParsedReviewResponse parsed;
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
                                content = prompt
                            }
                        }
                    })
                };

                using var response = await httpClient.SendAsync(request, cancellationToken);
                httpStatus = (int)response.StatusCode;
                body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Local targeted review returned HTTP {(int)response.StatusCode}.");

                parsed = ParseApiResponse(body);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                stopwatch.Stop();
                _logger.Error(
                    "review_window_error",
                    ("model", model),
                    ("windowIndex", oneBasedWindow),
                    ("windowCount", windows.Count),
                    ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                    ("httpStatus", httpStatus),
                    ("responseChars", body.Length),
                    ("category", ex.GetType().Name),
                    ("reasonCode", ex is HttpRequestException ? "http_error" : "invalid_response"),
                    ("result", "fallback"));
                status?.Report("Enhanced: korekta Qwen nie powiodła się — zachowuję tłumaczenie bazowe.");
                return output;
            }

            var applied = 0;
            var dropped = 0;
            foreach (var (id, text) in parsed.Changes)
            {
                if (!allowedIds.Contains(id) || !outputPositionById.TryGetValue(id, out var position))
                {
                    dropped++;
                    continue;
                }
                output[position] = output[position] with { Text = text };
                applied++;
            }

            stopwatch.Stop();
            _logger.Info(
                "review_window_end",
                ("model", model),
                ("windowIndex", oneBasedWindow),
                ("windowCount", windows.Count),
                ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                ("httpStatus", httpStatus),
                ("responseChars", body.Length),
                ("promptTokens", parsed.PromptTokens),
                ("completionTokens", parsed.CompletionTokens),
                ("finishReason", parsed.FinishReason),
                ("completed", applied),
                ("dropped", dropped),
                ("result", "success"));

            progress?.Report((double)oneBasedWindow / windows.Count);
            status?.Report($"Enhanced: korekta kontekstu {oneBasedWindow} / {windows.Count}…");
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

    private static ParsedReviewResponse ParseApiResponse(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var choice = root.GetProperty("choices")[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString()
                : null;
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Targeted gender review reached its token limit.");

            var content = choice.GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("Targeted gender review returned no content.");

            int? promptTokens = null;
            int? completionTokens = null;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var promptTokensElement) && promptTokensElement.TryGetInt32(out var parsedPromptTokens))
                    promptTokens = parsedPromptTokens;
                if (usage.TryGetProperty("completion_tokens", out var completionTokensElement) && completionTokensElement.TryGetInt32(out var parsedCompletionTokens))
                    completionTokens = parsedCompletionTokens;
            }

            return new ParsedReviewResponse(
                GenderReviewProtocol.ParseResponse(content),
                finishReason,
                promptTokens,
                completionTokens);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Targeted gender review returned invalid API JSON.", ex);
        }
    }

    private sealed record ParsedReviewResponse(
        IReadOnlyDictionary<int, string> Changes,
        string? FinishReason,
        int? PromptTokens,
        int? CompletionTokens);
}
