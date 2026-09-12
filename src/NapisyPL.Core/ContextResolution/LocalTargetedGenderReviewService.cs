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
    int maxCandidatesPerBatch = 5,
    int maxContextCuesPerBatch = 25)
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
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, SpeakerGenderEvidence>? speakerGenderEvidence = null)
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

            var probableAddressees = allowedIds.ToDictionary(
                id => id,
                id => DialogueAddresseeResolver.Resolve(source, cueSpeakers, id));

            var relevantSpeakerIds = allowedIds
                .Select(id => cueSpeakers.TryGetValue(id, out var speaker) ? speaker : null)
                .Concat(probableAddressees.Values)
                .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
                .Select(speaker => speaker!)
                .ToHashSet(StringComparer.Ordinal);
            var speakerSamples = allSpeakerSamples
                .Where(pair => relevantSpeakerIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var relevantGenderEvidence = (speakerGenderEvidence ?? new Dictionary<string, SpeakerGenderEvidence>())
                .Where(pair => relevantSpeakerIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var prompt = TargetedGenderReviewProtocol.BuildPrompt(
                sourceWindow,
                translatedWindow,
                allowedIds,
                cueSpeakers,
                speakerSamples,
                probableAddressees,
                relevantGenderEvidence);

            var oneBasedBatch = batchIndex + 1;
            _logger.Info(
                "review_window_start",
                ("model", model),
                ("windowIndex", oneBasedBatch),
                ("windowCount", batches.Count),
                ("cueCount", sourceWindow.Count),
                ("candidateCount", allowedIds.Count),
                ("speakerCount", speakerSamples.Count),
                ("genderEvidenceCount", relevantGenderEvidence.Count),
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
                var isGptOss = string.Equals(model, "gpt-oss-20b", StringComparison.OrdinalIgnoreCase);
                object chatTemplateKwargs = isGptOss
                    ? new { reasoning_effort = "low" }
                    : new { enable_thinking = false };
                object responseFormat = isGptOss
                    ? new { type = "json_object" }
                    : LlamaJsonSchemas.SurgicalReviewResponseFormat;
                var systemMessage = isGptOss
                    ? "Return one JSON object with exactly one top-level property named edits. edits must be an array of tiny exact Polish gender/number replacements with id, find, replace, confidence, and target. Never rewrite subtitle lines. Example empty result: {\"edits\":[]}."
                    : "Return only tiny exact Polish gender/number replacements. Label every edit target as speaker or addressee. Never rewrite subtitle lines.";

                using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
                {
                    Content = JsonContent.Create(new
                    {
                        model,
                        temperature = 0.0,
                        max_tokens = 400,
                        reasoning_effort = isGptOss ? "low" : "none",
                        chat_template_kwargs = chatTemplateKwargs,
                        response_format = responseFormat,
                        messages = new object[]
                        {
                            new { role = "system", content = systemMessage },
                            new { role = "user", content = prompt }
                        }
                    })
                };

                using var response = await httpClient.SendAsync(request, requestCancellation.Token);
                httpStatus = (int)response.StatusCode;
                body = await response.Content.ReadAsStringAsync(requestCancellation.Token);
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"Local targeted review returned HTTP {(int)response.StatusCode}.");

                parsed = ParseApiResponse(body, isGptOss);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                LogWindowError(oneBasedBatch, batches.Count, stopwatch.Elapsed.TotalMilliseconds, httpStatus, body.Length, nameof(TimeoutException), "review_timeout");
                progress?.Report((double)oneBasedBatch / batches.Count);
                status?.Report($"Enhanced: paczka {oneBasedBatch} przekroczyła limit czasu — pomijam ją i kontynuuję.");
                continue;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException)
            {
                stopwatch.Stop();
                LogWindowError(
                    oneBasedBatch,
                    batches.Count,
                    stopwatch.Elapsed.TotalMilliseconds,
                    httpStatus,
                    body.Length,
                    ex.GetType().Name,
                    ex is HttpRequestException ? "http_error" : ClassifyInvalidResponse((InvalidDataException)ex));
                progress?.Report((double)oneBasedBatch / batches.Count);
                status?.Report($"Enhanced: paczka {oneBasedBatch} zwróciła błąd — zachowuję bazowe tłumaczenie tej paczki i kontynuuję.");
                continue;
            }

            var applied = 0;
            var dropped = 0;
            var droppedOutsideBatch = 0;
            var droppedMissingSpeaker = 0;
            var droppedContextGuard = 0;
            var droppedApplyGuard = 0;
            var droppedApplyCueMismatch = 0;
            var droppedApplyConfidence = 0;
            var droppedApplyFragment = 0;
            var droppedApplyUnchanged = 0;
            var droppedApplyInflection = 0;
            var droppedApplyFindMissing = 0;
            var droppedApplyFindAmbiguous = 0;
            var droppedApplyThirdPersonSubjectConflict = 0;

            foreach (var edit in parsed.Edits)
            {
                if (!allowedIds.Contains(edit.Id) || !outputPositionById.TryGetValue(edit.Id, out var position))
                {
                    dropped++;
                    droppedOutsideBatch++;
                    continue;
                }

                cueSpeakers.TryGetValue(edit.Id, out var currentSpeaker);
                probableAddressees.TryGetValue(edit.Id, out var probableAddressee);
                if (string.IsNullOrWhiteSpace(currentSpeaker))
                {
                    dropped++;
                    droppedMissingSpeaker++;
                    continue;
                }

                SpeakerGenderEvidence? currentSpeakerGenderEvidence = null;
                speakerGenderEvidence?.TryGetValue(currentSpeaker!, out currentSpeakerGenderEvidence);

                SpeakerGenderEvidence? probableAddresseeGenderEvidence = null;
                if (!string.IsNullOrWhiteSpace(probableAddressee))
                    speakerGenderEvidence?.TryGetValue(probableAddressee!, out probableAddresseeGenderEvidence);

                if (!SurgicalGenderContextGuard.CanApply(
                        edit,
                        currentSpeaker,
                        probableAddressee,
                        currentSpeakerGenderEvidence,
                        probableAddresseeGenderEvidence))
                {
                    dropped++;
                    droppedContextGuard++;
                    continue;
                }

                if (!SurgicalGenderEditApplier.TryApply(output[position], edit, out var changed, out var rejectReason))
                {
                    dropped++;
                    droppedApplyGuard++;
                    switch (rejectReason)
                    {
                        case SurgicalGenderEditRejectReason.CueMismatch: droppedApplyCueMismatch++; break;
                        case SurgicalGenderEditRejectReason.LowConfidence: droppedApplyConfidence++; break;
                        case SurgicalGenderEditRejectReason.InvalidFragment: droppedApplyFragment++; break;
                        case SurgicalGenderEditRejectReason.Unchanged: droppedApplyUnchanged++; break;
                        case SurgicalGenderEditRejectReason.NotInflectionOnly: droppedApplyInflection++; break;
                        case SurgicalGenderEditRejectReason.FindMissing: droppedApplyFindMissing++; break;
                        case SurgicalGenderEditRejectReason.FindAmbiguous: droppedApplyFindAmbiguous++; break;
                        case SurgicalGenderEditRejectReason.ThirdPersonSubjectConflict: droppedApplyThirdPersonSubjectConflict++; break;
                    }
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
                ("proposed", parsed.Edits.Count),
                ("completed", applied),
                ("dropped", dropped),
                ("dropOutsideBatch", droppedOutsideBatch),
                ("dropMissingSpeaker", droppedMissingSpeaker),
                ("dropContextGuard", droppedContextGuard),
                ("dropApplyGuard", droppedApplyGuard),
                ("dropApplyCueMismatch", droppedApplyCueMismatch),
                ("dropApplyConfidence", droppedApplyConfidence),
                ("dropApplyFragment", droppedApplyFragment),
                ("dropApplyUnchanged", droppedApplyUnchanged),
                ("dropApplyInflection", droppedApplyInflection),
                ("dropApplyFindMissing", droppedApplyFindMissing),
                ("dropApplyFindAmbiguous", droppedApplyFindAmbiguous),
                ("dropApplyThirdPersonSubjectConflict", droppedApplyThirdPersonSubjectConflict),
                ("result", "success"));

            progress?.Report((double)oneBasedBatch / batches.Count);
            status?.Report($"Enhanced: korekta kontekstu {oneBasedBatch} / {batches.Count}…");
        }

        return output;
    }

    private void LogWindowError(int windowIndex, int windowCount, double elapsedMs, int? httpStatus, int responseChars, string category, string reasonCode)
    {
        _logger.Error(
            "review_window_error",
            ("model", model),
            ("windowIndex", windowIndex),
            ("windowCount", windowCount),
            ("elapsedMs", elapsedMs),
            ("httpStatus", httpStatus),
            ("responseChars", responseChars),
            ("category", category),
            ("reasonCode", reasonCode),
            ("result", "fallback"));
    }

    private static string ClassifyInvalidResponse(InvalidDataException exception)
    {
        var message = exception.Message;
        if (message.Contains("token limit", StringComparison.OrdinalIgnoreCase)) return "review_token_limit";
        if (message.Contains("no content", StringComparison.OrdinalIgnoreCase)) return "review_no_content";
        if (message.Contains("API JSON", StringComparison.OrdinalIgnoreCase)) return "review_api_json";
        if (message.Contains("agreement target", StringComparison.OrdinalIgnoreCase)) return "review_target_schema";
        if (message.Contains("JSON", StringComparison.OrdinalIgnoreCase)) return "review_edit_json";
        return "invalid_response";
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

    private static ParsedReviewResponse ParseApiResponse(string body, bool gptOssWrappedObject)
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

            var edits = gptOssWrappedObject
                ? ParseWrappedReviewEdits(content)
                : ParseReviewEdits(content);
            return new ParsedReviewResponse(edits, finishReason, promptTokens, completionTokens);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Targeted gender review returned invalid API JSON.", ex);
        }
    }

    private static IReadOnlyList<SurgicalGenderEdit> ParseWrappedReviewEdits(string content)
    {
        var json = StripFence(content.Trim());
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("edits", out var edits) ||
                edits.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Gender reviewer returned invalid wrapped JSON.");
            return SurgicalGenderEditProtocol.ParseResponse(edits.GetRawText());
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Gender reviewer returned invalid wrapped JSON.", ex);
        }
    }

    private static IReadOnlyList<SurgicalGenderEdit> ParseReviewEdits(string content)
    {
        try
        {
            return SurgicalGenderEditProtocol.ParseResponse(content);
        }
        catch (InvalidDataException)
        {
            var firstArray = content.IndexOf('[');
            var lastArray = content.LastIndexOf(']');
            if (firstArray < 0 || lastArray <= firstArray)
                throw;

            var extracted = content[firstArray..(lastArray + 1)];
            if (string.Equals(extracted.Trim(), content.Trim(), StringComparison.Ordinal))
                throw;

            return SurgicalGenderEditProtocol.ParseResponse(extracted);
        }
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstNewLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? text[(firstNewLine + 1)..lastFence].Trim()
            : text;
    }

    private sealed record ParsedReviewResponse(
        IReadOnlyList<SurgicalGenderEdit> Edits,
        string? FinishReason,
        int? PromptTokens,
        int? CompletionTokens);
}
