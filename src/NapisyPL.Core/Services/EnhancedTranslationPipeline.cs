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
    LocalContextRuntimeManager runtimeManager,
    LocalTargetedGenderReviewService targetedReview,
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

            // Audio is used only to keep stable speaker identities. No LLM work happens before translation.
            var speakers = await speakerAnalysis.AnalyzeAsync(
                inputPath,
                sourceCues,
                diarizationProgress: null,
                status,
                cancellationToken);

            status?.Report($"Enhanced: wykryto {speakers.SpeakerSegmentCount} fragmentów mowy. Tłumaczę przez {provider.DisplayName}…");
            var timer = Stopwatch.StartNew();
            var translated = translationProgress is null
                ? await translationCoordinator.TranslateCuesAsync(sourceCues, provider, progress: (IProgress<double>?)null, cancellationToken)
                : await translationCoordinator.TranslateCuesAsync(sourceCues, provider, translationProgress, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "translation"), ("provider", provider.DisplayName), ("elapsedMs", timer.ElapsedMilliseconds), ("segmentCount", translated.Count), ("result", "success"));

            cancellationToken.ThrowIfCancellationRequested();
            var candidateIds = GenderReviewCandidateSelector.SelectCandidateIds(translated);
            IReadOnlyList<SubtitleCue> reviewed = translated;
            var changedCount = 0;

            if (candidateIds.Count > 0)
            {
                status?.Report($"Enhanced: {candidateIds.Count} kwestii może wymagać korekty rodzaju — uruchamiam lokalny korektor…");
                timer.Restart();
                await runtimeManager.EnsureRunningAsync(status, cancellationToken);
                logger?.Info("enhanced_phase", ("file", file), ("stage", "reviewer_startup"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));

                timer.Restart();
                reviewed = await targetedReview.ReviewAsync(
                    sourceCues,
                    translated,
                    speakers.CueSpeakers,
                    progress: null,
                    status,
                    cancellationToken);
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
}
