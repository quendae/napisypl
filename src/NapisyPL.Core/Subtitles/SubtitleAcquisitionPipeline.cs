using System.ComponentModel;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Subtitles;

/// <summary>Resolves video subtitles before invoking any translation provider.</summary>
public sealed class SubtitleAcquisitionPipeline(
    IVideoSubtitleTranslationPipeline translationPipeline,
    ISubtitleDownloader downloader) : ITranslationPipeline
{
    private readonly SrtParser _parser = new();
    private readonly SubtitleWriter _writer = new();
    public bool Enabled { get; set; } = true;

    public async Task<TranslationResult> TranslateAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enabled || !TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(inputPath)))
            return await translationPipeline.TranslateAsync(inputPath, selectedTrack, provider, exportTxt,
                translationProgress, status, cancellationToken);

        var output = Path.Combine(Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory,
            Path.GetFileNameWithoutExtension(inputPath) + ".pl.srt");
        if (File.Exists(output))
        {
            var existing = _parser.Parse(await File.ReadAllTextAsync(output, cancellationToken));
            ValidateCues(existing);
            status?.Report("Zachowuję istniejące polskie napisy.");
            return await SavePolishAsync(output, existing, exportTxt, writeSrt: false, cancellationToken);
        }

        var failures = new List<string>();
        foreach (var language in new[] { SubtitleLanguage.Polish, SubtitleLanguage.English })
        {
            var label = language == SubtitleLanguage.Polish ? "PL" : "EN";
            status?.Report($"QNapi: szukam napisów {label}…");
            DownloadedSubtitles? found;
            try
            {
                found = await downloader.DownloadAsync(inputPath, language, status, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (found is null)
                {
                    status?.Report($"QNapi: nie znaleziono napisów {label}.");
                    continue;
                }
                if (found.Language != language)
                    throw new InvalidDataException($"Źródło zwróciło inny język niż {label}.");
                ValidateCues(found.Cues);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or TimeoutException
                                       or UnauthorizedAccessException or Win32Exception or NotSupportedException)
            {
                failures.Add($"{label}: {ex.Message}");
                status?.Report($"Wyszukiwanie {label} nie powiodło się: {ex.Message}");
                continue;
            }

            if (language == SubtitleLanguage.Polish)
            {
                status?.Report($"Znaleziono polskie napisy ({found.Provider}) — zapisuję bez tłumaczenia.");
                return await SavePolishAsync(output, found.Cues, exportTxt, writeSrt: true, cancellationToken);
            }

            status?.Report($"Znaleziono angielskie napisy ({found.Provider}) — tłumaczę z zachowaniem audio filmu.");
            return await translationPipeline.TranslateVideoSubtitlesAsync(inputPath, found.Cues, provider,
                exportTxt, translationProgress, status, cancellationToken);
        }

        if (selectedTrack is { IsText: true })
        {
            status?.Report("Brak pobranych napisów — używam wybranej ścieżki z filmu.");
            return await translationPipeline.TranslateAsync(inputPath, selectedTrack, provider, exportTxt,
                translationProgress, status, cancellationToken);
        }

        var reason = failures.Count > 0 ? " Wyszukiwanie napotkało błędy: " + string.Join("; ", failures) : string.Empty;
        throw new InvalidOperationException("Nie znaleziono napisów PL ani EN i film nie ma wybranej tekstowej ścieżki. " +
            "Wczytaj pasujący plik SRT lub spróbuj ponownie później." + reason);
    }

    private async Task<TranslationResult> SavePolishAsync(string output, IReadOnlyList<SubtitleCue> cues,
        bool exportTxt, bool writeSrt, CancellationToken cancellationToken)
    {
        if (writeSrt)
            await WriteNewFileAsync(output, path => _writer.WriteSrtAsync(path, cues, cancellationToken), cancellationToken);
        string? textOutput = null;
        if (exportTxt)
        {
            textOutput = Path.ChangeExtension(output, ".txt");
            if (!File.Exists(textOutput))
                await WriteNewFileAsync(textOutput, path => _writer.WriteTxtAsync(path, cues, cancellationToken), cancellationToken);
        }
        return new TranslationResult(output, textOutput, cues.Count);
    }

    private static async Task WriteNewFileAsync(string output, Func<string, Task> write, CancellationToken cancellationToken)
    {
        var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await write(temporary);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, output, overwrite: false);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateCues(IReadOnlyList<SubtitleCue> cues)
    {
        if (cues.Count == 0 || cues.Any(c => c.Start < TimeSpan.Zero || c.End <= c.Start || string.IsNullOrWhiteSpace(c.Text))
            || cues.Select(c => c.Index).Distinct().Count() != cues.Count)
            throw new InvalidDataException("Plik napisów jest pusty lub ma nieprawidłowe czasy/numery kwestii; istniejący plik nie zostanie nadpisany.");
    }
}
