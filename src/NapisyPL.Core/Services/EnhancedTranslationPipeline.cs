using System.Diagnostics;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.LocalTranslation;
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
    LocalContextRuntimeManager runtimeManager,
    LocalTargetedGenderReviewService targetedReview,
    IAppLogger? logger = null,
    EnhancedTranslationCache? translationCache = null) : ITranslationPipeline
{
    private const string ArgosDisplayName = "Argos EN→PL";
    private readonly EnhancedTranslationCache _translationCache = translationCache ?? EnhancedTranslationCache.CreateDefault();

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

        var file = Path.GetFileName(inputPath);
        BeginRuntimeWarmup(runtimeManager, logger, file);

        string? temporarySrt = null;
        try
        {
            status?.Report("Enhanced: wyciągam napisy z filmu…");
            temporarySrt = await extractionService.ExtractToTemporarySrtAsync(
                inputPath, selectedTrack, status, cancellationToken);

            var content = await File.ReadAllTextAsync(temporarySrt, Encoding.UTF8, cancellationToken);
            var sourceCues = srtParser.Parse(content);
            if (sourceCues.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się odczytać żadnych kwestii z napisów.");

            // Audio keeps stable speaker identities and supplies conservative acoustic gender evidence.
            var speakers = await speakerAnalysis.AnalyzeAsync(
                inputPath,
                sourceCues,
                diarizationProgress: null,
                status,
                cancellationToken);

            var knownGenderCount = speakers.SpeakerGenderEvidence.Count(pair => pair.Value.Gender != SpeakerVoiceGender.Unknown);
            status?.Report($"Enhanced: wykryto {speakers.SpeakerSegmentCount} fragmentów mowy, pewna klasyfikacja głosu dla {knownGenderCount} rozmówców. Tłumaczę przez {provider.DisplayName}…");
            var timer = Stopwatch.StartNew();
            var translated = await TranslateWithOptionalArgosCacheAsync(
                sourceCues,
                provider,
                translationProgress,
                status,
                cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "translation"), ("provider", provider.DisplayName), ("elapsedMs", timer.ElapsedMilliseconds), ("segmentCount", translated.Count), ("result", "success"));

            cancellationToken.ThrowIfCancellationRequested();
            var candidateIds = GenderReviewCandidateSelector.SelectCandidateIds(translated);
            IReadOnlyList<SubtitleCue> reviewed = translated;
            var changedCount = 0;

            if (candidateIds.Count > 0)
            {
                LogReviewCoverage(
                    logger,
                    sourceCues,
                    candidateIds,
                    speakers.CueSpeakers,
                    speakers.SpeakerGenderEvidence);

                status?.Report($"Enhanced: {candidateIds.Count} kwestii może wymagać korekty rodzaju — przygotowuję lokalny korektor…");
                timer.Restart();
                await runtimeManager.EnsureRunningAsync(status, cancellationToken);
                logger?.Info("enhanced_phase", ("file", file), ("stage", "reviewer_startup"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));

                timer.Restart();
                reviewed = await targetedReview.ReviewAsync(
                    sourceCues,
                    translated,
                    speakers.CueSpeakers,
                    progress: null,
                    status: status,
                    cancellationToken: cancellationToken,
                    speakerGenderEvidence: speakers.SpeakerGenderEvidence);
                changedCount = reviewed.Zip(translated).Count(pair => !string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal));
                logger?.Info(
                    "enhanced_phase",
                    ("file", file),
                    ("stage", "gender_review"),
                    ("elapsedMs", timer.ElapsedMilliseconds),
                    ("segmentCount", translated.Count),
                    ("candidateCount", candidateIds.Count),
                    ("completed", changedCount),
                    ("result", "success"));
            }
            else
            {
                logger?.Info("enhanced_phase", ("file", file), ("stage", "gender_review"), ("candidateCount", 0), ("completed", 0), ("result", "skipped"));
            }

            var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var srtOutput = Path.Combine(directory, stem + ".pl.srt");
            status?.Report($"Enhanced: zapisuję wynik — lokalny korektor zmienił {changedCount} kwestii…");
            await writer.WriteSrtAsync(srtOutput, reviewed, cancellationToken);

            string? txtOutput = null;
            if (exportTxt)
            {
                txtOutput = Path.Combine(directory, stem + ".pl.txt");
                await writer.WriteTxtAsync(txtOutput, reviewed, cancellationToken);
            }

            return new TranslationResult(srtOutput, txtOutput, reviewed.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.Error("enhanced_failed", ("file", file), ("stage", "translation_pipeline"), ("category", ex.GetType().Name), ("result", "failed"));
            throw;
        }
        finally
        {
            if (temporarySrt is not null)
            {
                try { File.Delete(temporarySrt); } catch { }
            }
        }
    }

    private async Task<IReadOnlyList<SubtitleCue>> TranslateWithOptionalArgosCacheAsync(
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? translationProgress,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(provider.DisplayName, ArgosDisplayName, StringComparison.Ordinal))
            return await TranslateNormallyAsync(sourceCues, provider, translationProgress, cancellationToken);

        var providerIdentity = BuildArgosCacheIdentity(provider);
        IReadOnlyList<SubtitleCue>? cached = null;
        try
        {
            cached = await _translationCache.TryLoadAsync(sourceCues, providerIdentity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Error(
                "translation_cache",
                ("provider", provider.DisplayName),
                ("segmentCount", sourceCues.Count),
                ("category", ex.GetType().Name),
                ("result", "read_error"));
        }

        logger?.Info(
            "translation_cache",
            ("provider", provider.DisplayName),
            ("segmentCount", sourceCues.Count),
            ("result", cached is null ? "miss" : "hit"));

        if (cached is not null)
        {
            status?.Report("Enhanced: używam zapisanego tłumaczenia Argos…");
            translationProgress?.Report(new TranslationProgress(
                sourceCues.Count,
                sourceCues.Count,
                1,
                1,
                false,
                DateTimeOffset.Now));
            return cached;
        }

        var translated = await TranslateNormallyAsync(sourceCues, provider, translationProgress, cancellationToken);
        try
        {
            await _translationCache.SaveAsync(sourceCues, translated, providerIdentity, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger?.Error(
                "translation_cache",
                ("provider", provider.DisplayName),
                ("segmentCount", sourceCues.Count),
                ("category", ex.GetType().Name),
                ("result", "write_error"));
        }

        return translated;
    }

    private Task<IReadOnlyList<SubtitleCue>> TranslateNormallyAsync(
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? translationProgress,
        CancellationToken cancellationToken) =>
        translationProgress is null
            ? translationCoordinator.TranslateCuesAsync(sourceCues, provider, progress: (IProgress<double>?)null, cancellationToken)
            : translationCoordinator.TranslateCuesAsync(sourceCues, provider, translationProgress, cancellationToken);

    private static string BuildArgosCacheIdentity(ITranslationProvider provider)
    {
        var options = ArgosRuntimeOptions.CreateDefault();
        return $"{provider.DisplayName}|{options.ModelFileName}|enhanced-cache-v1";
    }

    private static void LogReviewCoverage(
        IAppLogger? logger,
        IReadOnlyList<SubtitleCue> source,
        IReadOnlySet<int> candidateIds,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence)
    {
        if (logger is null)
            return;

        var coverage = GenderReviewCoverageDiagnostics.Summarize(
            source,
            candidateIds,
            cueSpeakers,
            speakerGenderEvidence);

        logger.Info(
            "review_coverage",
            ("candidateCount", coverage.CandidateCount),
            ("knownGenderEvidenceCount", coverage.KnownRelevantGenderEvidenceCount),
            ("knownSpeakerCandidateCount", coverage.KnownSpeakerCandidateCount),
            ("knownAddresseeCandidateCount", coverage.KnownAddresseeCandidateCount),
            ("knownSpeakerCandidateIds", string.Join(",", coverage.KnownSpeakerCandidateIds)),
            ("knownAddresseeCandidateIds", string.Join(",", coverage.KnownAddresseeCandidateIds)));

        var batches = GenderReviewCandidateSelector.BuildReviewBatches(
            source,
            candidateIds,
            radius: 2,
            maxCandidatesPerBatch: 5,
            maxContextCuesPerBatch: 25);
        for (var i = 0; i < batches.Count; i++)
        {
            var windowCoverage = GenderReviewCoverageDiagnostics.Summarize(
                source,
                batches[i].CandidateIds,
                cueSpeakers,
                speakerGenderEvidence);
            logger.Info(
                "review_window_coverage",
                ("windowIndex", i + 1),
                ("windowCount", batches.Count),
                ("candidateCount", windowCoverage.CandidateCount),
                ("knownGenderEvidenceCount", windowCoverage.KnownRelevantGenderEvidenceCount),
                ("knownSpeakerCandidateCount", windowCoverage.KnownSpeakerCandidateCount),
                ("knownAddresseeCandidateCount", windowCoverage.KnownAddresseeCandidateCount),
                ("knownSpeakerCandidateIds", string.Join(",", windowCoverage.KnownSpeakerCandidateIds)),
                ("knownAddresseeCandidateIds", string.Join(",", windowCoverage.KnownAddresseeCandidateIds)));
        }
    }

    private static void BeginRuntimeWarmup(
        LocalContextRuntimeManager runtimeManager,
        IAppLogger? logger,
        string file)
    {
        _ = WarmRuntimeAsync(runtimeManager, logger, file);
    }

    private static async Task WarmRuntimeAsync(
        LocalContextRuntimeManager runtimeManager,
        IAppLogger? logger,
        string file)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await runtimeManager.EnsureRunningAsync(status: null, CancellationToken.None);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "reviewer_preload"),
                ("elapsedMs", timer.ElapsedMilliseconds),
                ("result", "success"));
        }
        catch (OperationCanceledException)
        {
            // Configuration reset or application shutdown cancels the shared startup.
        }
        catch (Exception ex)
        {
            logger?.Error(
                "enhanced_failed",
                ("file", file),
                ("stage", "reviewer_preload"),
                ("category", ex.GetType().Name),
                ("result", "fallback"));
        }
    }
}
