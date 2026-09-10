using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Services;

public sealed class EnhancedTranslationPipeline(
    ITranslationPipeline standardPipeline,
    SubtitleExtractionService extractionService,
    SrtParser srtParser,
    SubtitleWriter writer,
    TranslationCoordinator translationCoordinator,
    EnhancedContextAnalysisService contextAnalysis,
    LocalContextRuntimeManager runtimeManager,
    LocalGenderReviewService genderReview) : ITranslationPipeline
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

            var analysis = await contextAnalysis.AnalyzeAsync(
                inputPath,
                sourceCues,
                diarizationProgress: null,
                resolverProgress: null,
                status,
                cancellationToken);

            status?.Report($"Enhanced: kontekst gotowy — {analysis.Context.Speakers.Count} profili mówców. Tłumaczę napisy…");
            var translated = translationProgress is null
                ? await translationCoordinator.TranslateCuesAsync(sourceCues, provider, progress: (IProgress<double>?)null, cancellationToken)
                : await translationCoordinator.TranslateCuesAsync(sourceCues, provider, translationProgress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Enhanced: sprawdzam rodzaj gramatyczny i adresatów…");
            await runtimeManager.EnsureRunningAsync(status, cancellationToken);
            var reviewed = await genderReview.ReviewAsync(
                sourceCues,
                translated,
                analysis.Context,
                analysis.CueSpeakers,
                progress: null,
                cancellationToken);

            var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var srtOutput = Path.Combine(directory, stem + ".pl.srt");
            status?.Report("Enhanced: zapisuję wynik…");
            await writer.WriteSrtAsync(srtOutput, reviewed, cancellationToken);

            string? txtOutput = null;
            if (exportTxt)
            {
                txtOutput = Path.Combine(directory, stem + ".pl.txt");
                await writer.WriteTxtAsync(txtOutput, reviewed, cancellationToken);
            }

            return new TranslationResult(srtOutput, txtOutput, reviewed.Count);
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
