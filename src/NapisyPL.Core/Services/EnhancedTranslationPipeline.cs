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
    IAppLogger? logger = null) : ITranslationPipeline
{
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

            status?.Report("Enhanced: analizuję rozmówców i lokalne zmiany głosu w audio…");
            var audioTimer = Stopwatch.StartNew();
            var speakers = await speakerAnalysis.AnalyzeAsync(
                inputPath,
                sourceCues,
                diarizationProgress: null,
                status,
                cancellationToken);
            var knownGenderCount = speakers.SpeakerGenderEvidence.Count(pair =>
                SpeakerGenderReviewEligibility.IsEligible(pair.Value));
            var knownCueGenderCount = speakers.CueGenderEvidence.Count(pair =>
                pair.Value.Gender != SpeakerVoiceGender.Unknown);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "speaker_analysis"),
                ("elapsedMs", audioTimer.ElapsedMilliseconds),
                ("segmentCount", speakers.SpeakerSegmentCount),
                ("knownGenderCount", knownGenderCount),
                ("knownCueGenderCount", knownCueGenderCount),
                ("result", "success"));

            status?.Report(
                $"Enhanced: {knownCueGenderCount}/{sourceCues.Count} wypowiedzi ma pewną lokalną klasyfikację głosu. Tłumaczę przez {provider.DisplayName}…");
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
            status?.Report("Enhanced: poprawiam tylko bezpieczne formy rodzaju na podstawie kolejności wypowiedzi…");
            var reviewTimer = Stopwatch.StartNew();
            var reviewed = deterministicReview.Review(
                sourceCues,
                translated,
                speakers.CueSpeakers,
                speakers.SpeakerGenderEvidence,
                speakers.CueGenderEvidence);
            var changedCount = reviewed.Zip(translated)
                .Count(pair => !string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal));
            var localTurnResolvedCount = sourceCues.Count(cue =>
                LocalTurnGenderResolver.Resolve(
                    sourceCues,
                    speakers.CueSpeakers,
                    speakers.CueGenderEvidence,
                    cue.Index,
                    speakers.SpeakerGenderEvidence).IsResolved);
            var resolvedAddresseeCount = sourceCues.Count(cue =>
                DialogueAddresseeResolver.ResolveDetailed(sourceCues, speakers.CueSpeakers, cue.Index).IsResolved);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "deterministic_gender_review"),
                ("elapsedMs", reviewTimer.ElapsedMilliseconds),
                ("segmentCount", translated.Count),
                ("knownGenderCount", knownGenderCount),
                ("knownCueGenderCount", knownCueGenderCount),
                ("localTurnResolvedCount", localTurnResolvedCount),
                ("resolvedAddresseeCount", resolvedAddresseeCount),
                ("completed", changedCount),
                ("result", "success"));

            var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var srtOutput = Path.Combine(directory, stem + ".pl.srt");
            status?.Report($"Enhanced: zapisuję wynik — deterministyczny korektor zmienił {changedCount} kwestii…");
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
            logger?.Error(
                "enhanced_failed",
                ("file", file),
                ("stage", "translation_pipeline"),
                ("category", ex.GetType().Name),
                ("result", "failed"));
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
