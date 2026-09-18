using System.Diagnostics;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Services;

public sealed class EnhancedTranslationPipeline(
    ITranslationPipeline standardPipeline,
    SubtitleExtractionService extractionService,
    SrtParser srtParser,
    SubtitleWriter writer,
    TranslationCoordinator translationCoordinator,
    SpeakerDiarizationAnalysisService speakerAnalysis,
    DeterministicGenderReviewService deterministicReview,
    IAppLogger? logger = null) : IVideoSubtitleTranslationPipeline
{
    public bool HardVoiceTurnOnly { get; set; }

    /// <summary>Leave the lines the review could not settle for a person instead of letting them stand.</summary>
    public bool AskAboutUncertainLines { get; set; } = true;

    public async Task<TranslationResult> TranslateAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(inputPath)))
        {
            status?.Report("Enhanced wymaga ścieżki audio; dla tego pliku używam trybu Standard.");
            return await standardPipeline.TranslateAsync(
                inputPath, selectedTrack, provider, exportTxt,
                translationProgress, status, cancellationToken);
        }

        if (selectedTrack is null || !selectedTrack.IsText)
            throw new InvalidOperationException("Enhanced wymaga tekstowej ścieżki napisów.");

        LogTranslationOptions(inputPath, provider, exportTxt);
        IReadOnlyList<SubtitleCue> sourceCues;
        try
        {
            sourceCues = await ExtractSourceCuesAsync(inputPath, selectedTrack, status, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(Path.GetFileName(inputPath), ex);
            throw;
        }

        return await TranslateVideoSubtitlesCoreAsync(
            inputPath,
            sourceCues,
            provider,
            exportTxt,
            translationProgress,
            status,
            cancellationToken,
            logOptions: false);
    }

    public Task<TranslationResult> TranslateVideoSubtitlesAsync(
        string videoPath,
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(videoPath)))
            throw new NotSupportedException("Enhanced wymaga ścieżki pliku wideo.");

        return TranslateVideoSubtitlesCoreAsync(
            videoPath,
            sourceCues,
            provider,
            exportTxt,
            translationProgress,
            status,
            cancellationToken,
            logOptions: true);
    }

    private async Task<IReadOnlyList<SubtitleCue>> ExtractSourceCuesAsync(
        string inputPath,
        SubtitleTrack selectedTrack,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        string? temporarySrt = null;
        try
        {
            status?.Report("Enhanced: wyciągam napisy z filmu…");
            temporarySrt = await extractionService.ExtractToTemporarySrtAsync(
                inputPath, selectedTrack, status, cancellationToken);

            var content = await File.ReadAllTextAsync(temporarySrt, Encoding.UTF8, cancellationToken);
            return srtParser.Parse(content);
        }
        finally
        {
            if (temporarySrt is not null)
            {
                try { File.Delete(temporarySrt); } catch { }
            }
        }
    }

    private async Task<TranslationResult> TranslateVideoSubtitlesCoreAsync(
        string inputPath,
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress,
        IProgress<string>? status,
        CancellationToken cancellationToken,
        bool logOptions)
    {
        var file = Path.GetFileName(inputPath);
        if (logOptions)
            LogTranslationOptions(inputPath, provider, exportTxt);

        try
        {
            if (sourceCues.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się odczytać żadnych kwestii z napisów.");

            status?.Report($"Enhanced: najpierw tłumaczę przez {provider.DisplayName}…");
            var translationTimer = Stopwatch.StartNew();
            var translated = await TranslateNormallyAsync(
                sourceCues,
                provider,
                translationProgress,
                cancellationToken);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "translation"),
                ("provider", provider.DisplayName),
                ("elapsedMs", translationTimer.ElapsedMilliseconds),
                ("segmentCount", translated.Count),
                ("result", "success"));

            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Enhanced: tłumaczenie gotowe — teraz analizuję głosy M/K w audio…");
            var audioTimer = Stopwatch.StartNew();
            var relevantCueIds = DeterministicGenderReviewService.CollectRelevantCueIds(sourceCues, translated);
            var speakers = await speakerAnalysis.AnalyzeAsync(
                inputPath,
                sourceCues,
                diarizationProgress: null,
                status,
                cancellationToken,
                relevantCueIds);
            var knownGenderCount = speakers.SpeakerGenderEvidence.Count(pair =>
                SpeakerGenderReviewEligibility.IsEligible(pair.Value));
            var knownCueGenderCount = speakers.CueGenderEvidence.Count(pair =>
                pair.Value.Gender != SpeakerVoiceGender.Unknown);
            var directionalCueGenderCount = speakers.CueGenderEvidence.Count(pair =>
                HardVoiceTurnResolver.TryGetForcedGender(
                    speakers.CueGenderEvidence,
                    pair.Key,
                    out _,
                    out _));
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "speaker_analysis"),
                ("elapsedMs", audioTimer.ElapsedMilliseconds),
                ("segmentCount", speakers.SpeakerSegmentCount),
                ("knownGenderCount", knownGenderCount),
                ("knownCueGenderCount", knownCueGenderCount),
                ("directionalCueGenderCount", directionalCueGenderCount),
                ("cueCount", relevantCueIds.Count),
                ("result", "success"));

            // SDH speaker labels ("JACLYN:") are read from the English text and outrank audio.
            var speakerLabels = SpeakerLabelEvidence.Continue(
                SpeakerLabelEvidence.Analyze(sourceCues, speakers.CueGenderEvidence),
                sourceCues,
                speakers.CueSpeakers,
                speakers.CueGenderEvidence);
            var (labeledCueEvidence, labeledSpeakerEvidence) = SpeakerLabelEvidence.Apply(
                speakerLabels, sourceCues, speakers.CueSpeakers, speakers.SpeakerGenderEvidence, speakers.CueGenderEvidence);
            speakers = speakers with
            {
                CueGenderEvidence = labeledCueEvidence,
                SpeakerGenderEvidence = labeledSpeakerEvidence
            };
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "speaker_labels"),
                ("labelCount", speakerLabels.CueLabel.Count),
                ("genderedLabelCount", speakerLabels.CueGender.Count),
                ("continuedLabelCount", speakerLabels.ContinuedGender?.Count ?? 0),
                ("result", "success"));

            status?.Report(HardVoiceTurnOnly
                ? $"Enhanced test M/K: {directionalCueGenderCount}/{sourceCues.Count} wypowiedzi ma zaakceptowaną klasyfikację głosu — sprawdzam stabilne profile rozmówców…"
                : $"Enhanced: {knownCueGenderCount}/{sourceCues.Count} wypowiedzi ma pewną lokalną klasyfikację głosu — poprawiam bezpieczne formy…");
            var reviewTimer = Stopwatch.StartNew();
            var genderDiagnostics = new List<DeterministicGenderCueDiagnostic>();
            var reviewed = deterministicReview.Review(
                sourceCues,
                translated,
                speakers.CueSpeakers,
                speakers.SpeakerGenderEvidence,
                speakers.CueGenderEvidence,
                genderDiagnostics,
                hardVoiceTurnOnly: HardVoiceTurnOnly,
                labeledCueGender: speakerLabels.AllCueGender);

            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Enhanced: końcowy Quality Pass EN↔PL — sprawdzam powtórki i artefakty…");
            var qualityTimer = Stopwatch.StartNew();
            var initialQualityIssues = FinalTranslationQualityGate.Evaluate(sourceCues, reviewed);
            var qualityRetryCount = 0;

            if (initialQualityIssues.Count > 0)
            {
                foreach (var issue in initialQualityIssues)
                {
                    logger?.Info(
                        "translation_quality_issue",
                        ("file", file),
                        ("cue", issue.CueId),
                        ("reason", issue.Reason),
                        ("sourceWordCount", issue.SourceWordCount),
                        ("outputWordCount", issue.OutputWordCount),
                        ("action", "retry_single_cue"));
                }

                status?.Report($"Quality Pass: wykryto {initialQualityIssues.Count} podejrzanych cue — ponawiam je pojedynczo przez {provider.DisplayName}…");
                var translatedById = translated.ToDictionary(cue => cue.Index);
                var sourceById = sourceCues.ToDictionary(cue => cue.Index);

                foreach (var issue in initialQualityIssues)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!sourceById.TryGetValue(issue.CueId, out var sourceCue))
                        continue;

                    var retry = await translationCoordinator.TranslateCuesAsync(
                        [sourceCue],
                        provider,
                        progress: (IProgress<double>?)null,
                        cancellationToken);
                    if (retry.Count != 1)
                        throw new InvalidDataException($"Quality Pass: retry cue {issue.CueId} returned {retry.Count} results.");

                    translatedById[issue.CueId] = retry[0];
                    qualityRetryCount++;
                }

                translated = sourceCues.Select(cue => translatedById[cue.Index]).ToArray();
                genderDiagnostics.Clear();
                reviewed = deterministicReview.Review(
                    sourceCues,
                    translated,
                    speakers.CueSpeakers,
                    speakers.SpeakerGenderEvidence,
                    speakers.CueGenderEvidence,
                    genderDiagnostics,
                    hardVoiceTurnOnly: HardVoiceTurnOnly,
                    labeledCueGender: speakerLabels.AllCueGender);
            }

            var finalQualityIssues = FinalTranslationQualityGate.Evaluate(sourceCues, reviewed);
            foreach (var issue in finalQualityIssues)
            {
                logger?.Info(
                    "translation_quality_issue",
                    ("file", file),
                    ("cue", issue.CueId),
                    ("reason", issue.Reason),
                    ("sourceWordCount", issue.SourceWordCount),
                    ("outputWordCount", issue.OutputWordCount),
                    ("action", "source_fallback_after_retry"));
            }

            var qualityFallbackCueIds = finalQualityIssues
                .Select(issue => issue.CueId)
                .ToHashSet();
            var qualityFallbackCueList = string.Join(", ", qualityFallbackCueIds.Order());
            if (finalQualityIssues.Count > 0)
            {
                reviewed = FinalTranslationQualityGate.ReplaceIssuesWithSource(
                    sourceCues,
                    reviewed,
                    finalQualityIssues);
                status?.Report(
                    $"Quality Pass: {finalQualityIssues.Count} cue nadal było uszkodzonych po retry — " +
                    $"pozostawiam oryginalny tekst angielski (cue: {qualityFallbackCueList}) i zapisuję pozostałe tłumaczenie.");
            }

            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "quality_pass"),
                ("elapsedMs", qualityTimer.ElapsedMilliseconds),
                ("initialIssueCount", initialQualityIssues.Count),
                ("retryCount", qualityRetryCount),
                ("remainingIssueCount", finalQualityIssues.Count),
                ("fallbackCount", finalQualityIssues.Count),
                ("result", finalQualityIssues.Count == 0 ? "success" : "source_fallback"));

            var changedCount = reviewed.Zip(translated)
                .Count(pair =>
                    !qualityFallbackCueIds.Contains(pair.First.Index) &&
                    !string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal));

            var sourcePositions = sourceCues
                .Select((cue, position) => (cue.Index, position))
                .ToDictionary(pair => pair.Index, pair => pair.position);
            foreach (var diagnostic in genderDiagnostics)
            {
                SubtitleCue? nextCue = null;
                if (sourcePositions.TryGetValue(diagnostic.CueId, out var position) && position + 1 < sourceCues.Count)
                    nextCue = sourceCues[position + 1];

                SpeakerGenderEvidence? currentSpeakerGender = null;
                if (!string.IsNullOrWhiteSpace(diagnostic.CurrentSpeaker) &&
                    speakers.SpeakerGenderEvidence.TryGetValue(diagnostic.CurrentSpeaker!, out var currentGender))
                {
                    currentSpeakerGender = currentGender;
                }

                speakers.CueGenderEvidence.TryGetValue(diagnostic.CueId, out var currentCueGender);
                var currentForcedGender = SpeakerVoiceGender.Unknown;
                var currentForcedConfidence = 0d;
                HardVoiceTurnResolver.TryGetForcedGender(
                    speakers.CueGenderEvidence,
                    diagnostic.CueId,
                    out currentForcedGender,
                    out currentForcedConfidence);

                string? nextSpeaker = null;
                CueVoiceGenderEvidence? nextCueGender = null;
                SpeakerGenderEvidence? nextSpeakerGender = null;
                var nextForcedGender = SpeakerVoiceGender.Unknown;
                var nextForcedConfidence = 0d;
                if (nextCue is not null)
                {
                    speakers.CueSpeakers.TryGetValue(nextCue.Index, out nextSpeaker);
                    if (speakers.CueGenderEvidence.TryGetValue(nextCue.Index, out var cueGender))
                        nextCueGender = cueGender;
                    HardVoiceTurnResolver.TryGetForcedGender(
                        speakers.CueGenderEvidence,
                        nextCue.Index,
                        out nextForcedGender,
                        out nextForcedConfidence);
                    if (!string.IsNullOrWhiteSpace(nextSpeaker) &&
                        speakers.SpeakerGenderEvidence.TryGetValue(nextSpeaker!, out var speakerGender))
                    {
                        nextSpeakerGender = speakerGender;
                    }
                }

                logger?.Info(
                    "enhanced_gender_cue",
                    ("cue", diagnostic.CueId),
                    ("speaker", diagnostic.CurrentSpeaker ?? "none"),
                    ("speakerGender", currentSpeakerGender?.Gender.ToString() ?? "none"),
                    ("speakerConfidencePermille", currentSpeakerGender is null ? 0 : ToPermille(currentSpeakerGender.Confidence)),
                    ("speakerSampleCount", currentSpeakerGender?.SampleCount ?? 0),
                    ("cueGender", currentCueGender?.Gender.ToString() ?? "none"),
                    ("cueDirectionalGender", currentCueGender?.DirectionalGender.ToString() ?? "none"),
                    ("cueDirectionalConfidencePermille", currentCueGender is null ? 0 : ToPermille(currentCueGender.DirectionalConfidence)),
                    ("forcedCueGender", currentForcedGender),
                    ("forcedCueConfidencePermille", ToPermille(currentForcedConfidence)),
                    ("candidate", diagnostic.CandidateWord ?? "none"),
                    ("resolver", diagnostic.Resolver),
                    ("reasonCode", diagnostic.ReasonCode),
                    ("targetGender", diagnostic.TargetGender),
                    ("confidencePermille", ToPermille(diagnostic.Confidence)),
                    ("gate", diagnostic.GatePassed ? "pass" : "fail"),
                    ("nextCue", nextCue?.Index ?? -1),
                    ("nextSpeaker", nextSpeaker ?? "none"),
                    ("nextCueGender", nextCueGender?.Gender.ToString() ?? "none"),
                    ("nextCueDirectionalGender", nextCueGender?.DirectionalGender.ToString() ?? "none"),
                    ("nextCueDirectionalConfidencePermille", nextCueGender is null ? 0 : ToPermille(nextCueGender.DirectionalConfidence)),
                    ("nextForcedCueGender", nextForcedGender),
                    ("nextForcedCueConfidencePermille", ToPermille(nextForcedConfidence)),
                    ("nextCueCombinedPermille", nextCueGender is null ? 0 : ToPermille(nextCueGender.CombinedEvidence)),
                    ("nextCueDurationMs", nextCueGender is null ? 0 : (int)Math.Round(nextCueGender.DurationSeconds * 1000)),
                    ("nextSpeakerGender", nextSpeakerGender?.Gender.ToString() ?? "none"),
                    ("nextSpeakerConfidencePermille", nextSpeakerGender is null ? 0 : ToPermille(nextSpeakerGender.Confidence)),
                    ("nextSpeakerSampleCount", nextSpeakerGender?.SampleCount ?? 0),
                    ("matchedWord", diagnostic.MatchedWord ?? "none"),
                    ("replacement", diagnostic.Replacement ?? "none"),
                    ("changed", diagnostic.Changed));
            }

            var hardVoiceResolvedCount = genderDiagnostics.Count(item =>
                !qualityFallbackCueIds.Contains(item.CueId) &&
                string.Equals(item.Resolver, "hard_voice_sequence", StringComparison.Ordinal) && item.GatePassed);
            var hardVoiceChangedCount = genderDiagnostics.Count(item =>
                !qualityFallbackCueIds.Contains(item.CueId) &&
                string.Equals(item.Resolver, "hard_voice_sequence", StringComparison.Ordinal) && item.Changed);
            // These two counters used to re-run the whole resolver stack across every
            // cue purely to log a number, doubling an already quadratic pass. The
            // review loop already visited each candidate, so read them off its
            // diagnostics instead.
            var localTurnResolvedCount = genderDiagnostics.Count(item =>
                string.Equals(item.Resolver, "local_turn", StringComparison.Ordinal));
            var resolvedAddresseeCount = genderDiagnostics.Count(item =>
                string.Equals(item.Resolver, "dialogue_addressee", StringComparison.Ordinal));
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "deterministic_gender_review"),
                ("reviewMode", HardVoiceTurnOnly ? "hard_voice_stable_evidence" : "standard"),
                ("elapsedMs", reviewTimer.ElapsedMilliseconds),
                ("segmentCount", translated.Count),
                ("knownGenderCount", knownGenderCount),
                ("knownCueGenderCount", knownCueGenderCount),
                ("directionalCueGenderCount", directionalCueGenderCount),
                ("hardVoiceResolvedCount", hardVoiceResolvedCount),
                ("hardVoiceChangedCount", hardVoiceChangedCount),
                ("localTurnResolvedCount", localTurnResolvedCount),
                ("resolvedAddresseeCount", resolvedAddresseeCount),
                ("candidateCount", genderDiagnostics.Count),
                ("completed", changedCount),
                ("result", "success"));

            var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var requiresReview = qualityFallbackCueIds.Count > 0;
            var outputSuffix = requiresReview ? ".needs-review.pl" : ".pl";
            var srtOutput = Path.Combine(directory, stem + outputSuffix + ".srt");
            status?.Report(requiresReview
                ? $"Quality Pass: zapisuję wynik do sprawdzenia — {qualityFallbackCueIds.Count} cue pozostało po angielsku…"
                : HardVoiceTurnOnly
                    ? $"Enhanced test M/K: Quality Pass OK, zapisuję wynik — zmieniono {changedCount} kwestii…"
                    : $"Enhanced: Quality Pass OK, zapisuję wynik — deterministyczny korektor zmienił {changedCount} kwestii…");
            await writer.WriteSrtAsync(srtOutput, reviewed, cancellationToken);

            // What the review could not settle goes next to the subtitles, for a person to answer -
            // unless the person asked to keep whatever the review decided on its own.
            if (AskAboutUncertainLines)
            {
                var openQuestions = DeterministicGenderReviewService.CollectOpenQuestions(
                    sourceCues, translated, reviewed, speakers.CueSpeakers, speakers.SpeakerGenderEvidence, speakers.CueGenderEvidence);
                await GenderReviewQueue.WriteAsync(
                    new GenderReviewQueueFile(inputPath, srtOutput, openQuestions), cancellationToken);
                logger?.Info(
                    "enhanced_phase",
                    ("file", file),
                    ("stage", "gender_questions"),
                    ("questionCount", openQuestions.Count),
                    ("result", "success"));
            }

            string? txtOutput = null;
            if (exportTxt)
            {
                txtOutput = Path.Combine(directory, stem + outputSuffix + ".txt");
                await writer.WriteTxtAsync(txtOutput, reviewed, cancellationToken);
            }

            var reviewMessage = requiresReview
                ? $"{qualityFallbackCueIds.Count} cue pozostało w oryginalnym języku po dwóch wadliwych wynikach modelu " +
                  $"(cue: {qualityFallbackCueList}). Plik wymaga sprawdzenia."
                : null;
            return new TranslationResult(srtOutput, txtOutput, reviewed.Count, requiresReview, reviewMessage);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailure(file, ex);
            throw;
        }
    }

    private void LogFailure(string file, Exception ex) =>
        logger?.Error(
            "enhanced_failed",
            ("file", file),
            ("stage", "translation_pipeline"),
            ("category", ex.GetType().Name),
            ("result", "failed"));

    private void LogTranslationOptions(string inputPath, ITranslationProvider provider, bool exportTxt) =>
        logger?.Info(
            "translation_options",
            ("file", Path.GetFileName(inputPath)),
            ("provider", provider.DisplayName),
            ("exportTxt", exportTxt),
            ("enhanced", true),
            ("hardVoiceMode", HardVoiceTurnOnly ? "hard_voice_stable_evidence" : "off"));

    private static int ToPermille(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 1000, MidpointRounding.AwayFromZero);

    private Task<IReadOnlyList<SubtitleCue>> TranslateNormallyAsync(
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? translationProgress,
        CancellationToken cancellationToken) =>
        translationProgress is null
            ? translationCoordinator.TranslateCuesAsync(
                sourceCues,
                provider,
                progress: (IProgress<double>?)null,
                cancellationToken)
            : translationCoordinator.TranslateCuesAsync(
                sourceCues,
                provider,
                translationProgress,
                cancellationToken);
}
