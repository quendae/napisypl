using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed class LocalGenderReviewService(
    HttpClient httpClient,
    string baseUrl,
    string model,
    int windowSize = 40)
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly int _windowSize = windowSize > 0 ? windowSize : throw new ArgumentOutOfRangeException(nameof(windowSize));

    public async Task<IReadOnlyList<SubtitleCue>> ReviewAsync(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        ContextMap context,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (source.Count != translated.Count)
            throw new ArgumentException("Source and translated cue counts must match.");
        if (source.Count == 0)
            return translated;

        var output = translated.ToArray();
        var outputPositionById = output
            .Select((cue, index) => (cue.Index, index))
            .ToDictionary(pair => pair.Index, pair => pair.index);
        var windowCount = (source.Count + _windowSize - 1) / _windowSize;

        for (var windowIndex = 0; windowIndex < windowCount; windowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var offset = windowIndex * _windowSize;
            var sourceWindow = source.Skip(offset).Take(_windowSize).ToArray();
            var translatedWindow = output.Skip(offset).Take(_windowSize).ToArray();
            var allowedIds = sourceWindow.Select(cue => cue.Index).ToHashSet();

            using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
            {
                Content = JsonContent.Create(new
                {
                    model,
                    temperature = 0.0,
                    max_tokens = 1400,
                    reasoning_effort = "none",
                    chat_template_kwargs = new { enable_thinking = false },
                    response_format = LlamaJsonSchemas.ReviewResponseFormat,
                    messages = new object[]
                    {
                        new
                        {
                            role = "system",
                            content = "Correct only Polish grammatical gender/number when supported by context. Return changed subtitle lines as JSON only."
                        },
                        new
                        {
                            role = "user",
                            content = GenderReviewProtocol.BuildPrompt(sourceWindow, translatedWindow, context, cueSpeakers)
                        }
                    }
                })
            };

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Local gender review: {(int)response.StatusCode} {body}");

            var changed = ParseApiResponse(body);
            foreach (var (id, text) in changed)
            {
                if (!allowedIds.Contains(id) || !outputPositionById.TryGetValue(id, out var position))
                    throw new InvalidDataException($"Gender reviewer attempted to change unexpected cue {id}.");
                output[position] = output[position] with { Text = text };
            }

            progress?.Report((double)(windowIndex + 1) / windowCount);
        }

        return output;
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
                throw new InvalidDataException("Local gender reviewer przerwał odpowiedź przez limit tokenów.");

            var content = choice.GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("Local gender reviewer returned no content.");
            return GenderReviewProtocol.ParseResponse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Local gender reviewer returned invalid API JSON.", ex);
        }
    }
}
