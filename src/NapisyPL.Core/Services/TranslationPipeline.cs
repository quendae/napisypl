using System.Text;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Services;

public sealed class TranslationPipeline(
    SrtParser srtParser,
    SubtitleWriter writer,
    SubtitleExtractionService extractionService,
    TranslationCoordinator coordinator) : ITranslationPipeline
{
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".mov", ".avi", ".webm", ".m4v", ".ts", ".mts", ".m2ts"
    };

    public static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".srt", ".ass", ".ssa", ".vtt", ".txt"
    };

    public bool UseEnhanced { get; set; }
    public ITranslationPipeline? EnhancedPipeline { get; set; }

    public static bool IsSupportedInput(string path)
    {
        var ext = Path.GetExtension(path);
        return VideoExtensions.Contains(ext) || SubtitleExtensions.Contains(ext);
    }

    public Task<TranslationResult> TranslateAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<double>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<TranslationProgress>? structuredProgress = translationProgress is null
            ? null
            : new InlineProgress<TranslationProgress>(value =>
            {
                if (!value.WaitingForProvider && value.TotalSegments > 0)
                    translationProgress.Report((double)value.CompletedSegments / value.TotalSegments);
            });

        return TranslateCoreAsync(inputPath, selectedTrack, provider, exportTxt, structuredProgress, status, cancellationToken);
    }

    Task<TranslationResult> ITranslationPipeline.TranslateAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress,
        IProgress<string>? status,
        CancellationToken cancellationToken) =>
        TranslateCoreAsync(inputPath, selectedTrack, provider, exportTxt, translationProgress, status, cancellationToken);

    private async Task<TranslationResult> TranslateCoreAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(inputPath);

        if (UseEnhanced && EnhancedPipeline is not null && VideoExtensions.Contains(extension))
        {
            return await EnhancedPipeline.TranslateAsync(
                inputPath,
                selectedTrack,
                provider,
                exportTxt,
                translationProgress,
                status,
                cancellationToken);
        }

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return await TranslateTextFileAsync(inputPath, provider, translationProgress, status, cancellationToken);

        string? temporarySrt = null;
        try
        {
            string srtPath;
            if (VideoExtensions.Contains(extension))
            {
                if (selectedTrack is null)
                    throw new InvalidOperationException("Wybierz tekstową ścieżkę napisów.");
                status?.Report("Wyciągam napisy z filmu…");
                temporarySrt = await extractionService.ExtractToTemporarySrtAsync(inputPath, selectedTrack, status, cancellationToken);
                srtPath = temporarySrt;
            }
            else if (extension.Equals(".srt", StringComparison.OrdinalIgnoreCase))
            {
                srtPath = inputPath;
            }
            else if (extension.Equals(".ass", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".ssa", StringComparison.OrdinalIgnoreCase) ||
                     extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase))
            {
                status?.Report("Konwertuję napisy do SRT…");
                temporarySrt = await extractionService.ConvertSubtitleFileToTemporarySrtAsync(inputPath, status, cancellationToken);
                srtPath = temporarySrt;
            }
            else
            {
                throw new NotSupportedException("Nieobsługiwany format pliku.");
            }

            var content = await File.ReadAllTextAsync(srtPath, Encoding.UTF8, cancellationToken);
            var cues = srtParser.Parse(content);
            if (cues.Count == 0)
                throw new InvalidDataException("Nie udało się odczytać żadnych kwestii z napisów.");

            status?.Report($"Tłumaczę {cues.Count} kwestii…");
            var translated = translationProgress is null
                ? await coordinator.TranslateCuesAsync(cues, provider, progress: (IProgress<double>?)null, cancellationToken)
                : await coordinator.TranslateCuesAsync(cues, provider, translationProgress, cancellationToken);
            var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var srtOutput = Path.Combine(directory, stem + ".pl.srt");
            status?.Report("Zapisuję wynik…");
            await writer.WriteSrtAsync(srtOutput, translated, cancellationToken);

            string? txtOutput = null;
            if (exportTxt)
            {
                txtOutput = Path.Combine(directory, stem + ".pl.txt");
                await writer.WriteTxtAsync(txtOutput, translated, cancellationToken);
            }
            return new TranslationResult(srtOutput, txtOutput, translated.Count);
        }
        finally
        {
            if (temporarySrt is not null)
            {
                try { File.Delete(temporarySrt); } catch { }
            }
        }
    }

    private async Task<TranslationResult> TranslateTextFileAsync(
        string inputPath,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? progress,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var lines = await File.ReadAllLinesAsync(inputPath, Encoding.UTF8, cancellationToken);
        status?.Report($"Tłumaczę {lines.Count(line => !string.IsNullOrWhiteSpace(line))} linii…");
        var translated = progress is null
            ? await coordinator.TranslateTextLinesAsync(lines, provider, progress: (IProgress<double>?)null, cancellationToken)
            : await coordinator.TranslateTextLinesAsync(lines, provider, progress, cancellationToken);
        var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var output = Path.Combine(directory, stem + ".pl.txt");
        await writer.WriteTxtAsync(output, translated, cancellationToken);
        return new TranslationResult(output, output, translated.Count(line => !string.IsNullOrWhiteSpace(line)));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
