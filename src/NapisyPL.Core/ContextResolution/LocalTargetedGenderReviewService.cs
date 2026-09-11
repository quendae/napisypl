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
    int contextRadius = 2,
    IAppLogger? logger = null,
    TimeSpan? requestTimeout = null,
    int maxCandidatesPerBatch = 10,
    int maxContextCuesPerBatch = 50)
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly int _contextRadius = contextRadius >= 0 ? contextRadius : throw new ArgumentOutOfRangeException(nameof(contextRadius));
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;
    private readonly TimeSpan _requestTimeout = ValidateTimeout(requestTimeout ?? TimeSpan.FromSeconds(90));
    private readonly int _maxCandidatesPerBatch = maxCandidatesPerBatch > 0 ? maxCandidatesPerBatch : throw new ArgumentOutOfRangeException(nameof(maxCandidatesPerBatch));
    private readonly int _maxContextCuesPerBatch = maxContextCuesPerBatch > 0 ? maxContextCuesPerBatch : throw new ArgumentOutOfRangeException(nameof(maxContextCuesPerBatch));

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
        var batches = GenderReviewCandidateSelector.BuildReviewBatches(
            source,
            candidateIds,
            _contextRadius,
            _maxCandidatesPerBatch,
            _maxContextCuesPerBatch);
        var allSpeakerSamples = BuildSpeakerSamples(source, cueSpeakers);

        _logger.Info(
            "review_plan",
            ("model", model),
            ("candidateCount", candidateIds.Count),
            ("windowCount", batches.Count),
            ("speakerCount", allSpeakerSamples.Count));
        status?.Report($"Enhanced: {candidateIds.Count} kwestii do sprawdzenia w {batches.Count} paczkach…");

        for (var batchIndex = 0; batchIndex < batches.Count; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches[batchIndex];
            var sourceWindow = batch.Cues;
            var allowedIds = batch.CandidateIds;
            var translatedWindow = sourceWindow.Select(cue => output[outputPositionById[cue.Index]]).ToArray();

            var candidateSpeakerIds = allowedIds
                .Select(id => cueSpeakers.TryGetValue(id, out var speaker) ? speaker : null)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .Select(speaker => speaker!)
                .ToHashSet(StringComparer.Ordinal);
            var speakerSamples = allSpeakerSamples
                .Where(pair => candidateSpeakerIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var prompt = TargetedGenderReviewProtocol.BuildPrompt(
                sourceWindow,
                translatedWindow,
                allowedIds,
                cueSpeakers,
                speakerSamples);

            var oneBasedBatch = batchIndex + 1;
            _logger.Info(
                "review_window_start",
                ("model", model),
                ("windowIndex", oneBasedBatch),
                ("windowCount", batches.Count),
                ("cueCount", sourceWindow.Count),
                ("candidateCount", allowedIds.Count),
                ("speakerCount", speakerSamples.Count),
                ("promptChars", prompt.Length));
            status?.Report($"Enhanced: model sprawdza paczkę {oneBasedBatch} / {batches.Count}…");

            var stopwatch = Stopwatch.StartNew();
            int? httpStatus = null;
            string body = string.Empty;
            ParsedReviewResponse parsed;
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestCancellation.CancelAfter(_requestTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
                {
                    Content = JsonContent.Create(new
                    {
                        model,
                        temperature = 0.0,
                        max_tokens = 400,
                        reasoning_effort = "none",
                        chat_template_kwargs = new { enable_thinking = false },
                        response_format = LlamaJsonSchemas.SurgicalReviewResponseFormat,
                        messages = new object[]
                        {
                            new
                            {
                                role = "system",
                                content = "Return only tiny exact Polish gender/number fragment replacements for allowed candidate IDs. Never rewrite subtitle lines."
                            },
                            new
                            {
                                role = "user",
                                content = prompt
                            }
                        }
                    })
                };

                using var response = await httpClient.SendAsync(request, requestCancellation.Token);
                httpStatus = (int)response.StatusCode;
                body = await response.Content.ReadAsStringAsync(requestCancellation.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Local targeted review returned HTTP {(int)response.StatusCode}.");

                parsed = ParseApiResponse(body);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                _logger.Error(
                    "review_window_error",
                    ("model", model),
                    ("windowIndex", oneBasedBatch),
                    ("windowCount", batches.Count),
                    ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                    ("httpStatus", httpStatus),
                    ("responseChars", body.Length),
                    ("category", nameof(TimeoutException)),
                    ("reasonCode", "review_timeout"),
                    ("result", "fallback"));
                status?.Report("Enhanced: model przekroczył limit czasu — zachowuję dotychczasowe tłumaczenie i kończę review.");
                return output;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                stopwatch.Stop();
                _logger.Error(
                    "review_window_error",
                    ("model", model),
                    ("windowIndex", oneBasedBatch),
                    ("windowCount", batches.Count),
                    ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                    ("httpStatus", httpStatus),
                    ("responseChars", body.Length),
                    ("category", ex.GetType().Name),
                    ("reasonCode", ex is HttpRequestException ? "http_error" : "invalid_response"),
                    ("result", "fallback"));
                status?.Report("Enhanced: korekta lokalna nie powiodła się — zachowuję dotychczasowe tłumaczenie.");
                return output;
            }

            var applied = 0;
            var dropped = 0;
            foreach (var edit in parsed.Edits)
            {
                if (!allowedIds.Contains(edit.Id) || !outputPositionById.TryGetValue(edit.Id, out var position))
                {
                    dropped++;
                    continue;
                }

                if (!SurgicalGenderEditApplier.TryApply(output[position], edit, out var changed))
                {
                    dropped++;
                    continue;
                }

                output[position] = changed;
                applied++;
            }

            stopwatch.Stop();
            _logger.Info(
                "review_window_end",
                ("model", model),
                ("windowIndex", oneBasedBatch),
                ("windowCount", batches.Count),
                ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                ("httpStatus", httpStatus),
                ("responseChars", body.Length),
                ("promptTokens", parsed.PromptTokens),
                ("completionTokens", parsed.CompletionTokens),
                ("finishReason", parsed.FinishReason),
                ("completed", applied),
                ("dropped", dropped),
                ("result", "success"));

            progress?.Report((double)oneBasedBatch / batches.Count);
            status?.Report($"Enhanced: korekta kontekstu {oneBasedBatch} / {batches.Count}…");
        }

        return output;
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        return timeout;
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
                    .Take(2)
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
                SurgicalGenderEditProtocol.ParseResponse(content),
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
        IReadOnlyList<SurgicalGenderEdit> Edits,
        string? FinishReason,
        int? PromptTokens,
        int? CompletionTokens);
}
