using System.ComponentModel;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Subtitles;

/// <summary>Resolves Polish subtitles before invoking any translation provider.</summary>
public sealed class SubtitleAcquisitionPipeline : ITranslationPipeline
{
    private readonly IVideoSubtitleTranslationPipeline _translationPipeline;
    private readonly ISubtitleDownloader _downloader;
    private readonly ISubtitleCueReader? _cueReader;
    private readonly SubtitleSynchronizationService? _synchronization;
    private readonly SrtParser _parser = new();
    private readonly SubtitleWriter _writer = new();

    public SubtitleAcquisitionPipeline(IVideoSubtitleTranslationPipeline translationPipeline, ISubtitleDownloader downloader)
    {
        _translationPipeline = translationPipeline ?? throw new ArgumentNullException(nameof(translationPipeline));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    }

    public SubtitleAcquisitionPipeline(
        IVideoSubtitleTranslationPipeline translationPipeline,
        ISubtitleDownloader downloader,
        ISubtitleCueReader cueReader,
        SubtitleSynchronizationService synchronization)
    {
        _translationPipeline = translationPipeline ?? throw new ArgumentNullException(nameof(translationPipeline));
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _cueReader = cueReader ?? throw new ArgumentNullException(nameof(cueReader));
        _synchronization = synchronization ?? throw new ArgumentNullException(nameof(synchronization));
    }

    public bool Enabled { get; set; } = true;

    public Task<TranslationResult> TranslateAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default) =>
        TranslateCoreAsync(inputPath, selectedTrack, provider, exportTxt, null, translationProgress, status, cancellationToken);

    public Task<TranslationResult> TranslateInteractiveAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ISubtitleFallbackInteraction fallbackInteraction,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fallbackInteraction);
        return TranslateCoreAsync(inputPath, selectedTrack, provider, exportTxt, fallbackInteraction,
            translationProgress, status, cancellationToken);
    }

    private async Task<TranslationResult> TranslateCoreAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        ISubtitleFallbackInteraction? fallbackInteraction,
        IProgress<TranslationProgress>? translationProgress,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enabled || !TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(inputPath)))
            return await _translationPipeline.TranslateAsync(inputPath, selectedTrack, provider, exportTxt,
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

        IReadOnlyList<SubtitleCue>? embeddedCues = null;
        var failures = new List<string>();
        if (selectedTrack is { IsText: true } && _cueReader is not null)
        {
            try
            {
                status?.Report("Odczytuję wybraną ścieżkę napisów z filmu…");
                embeddedCues = await _cueReader.ReadEmbeddedAsync(inputPath, selectedTrack, status, cancellationToken);
                ValidateCues(embeddedCues);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsHandledSubtitleException(ex))
            {
                failures.Add("embedded: " + ex.Message);
                status?.Report("Nie udało się odczytać wybranej ścieżki napisów: " + ex.Message);
            }
        }

        DownloadedSubtitles? automaticPolish = null;
        SubtitleTimingAnalysis? automaticAnalysis = null;
        var polishDownload = await TryDownloadAsync(inputPath, SubtitleLanguage.Polish, failures, status, cancellationToken);
        var downloadedPolish = polishDownload.Subtitles;
        if (downloadedPolish is not null)
        {
            automaticPolish = downloadedPolish;
            var prepared = PreparePolish(downloadedPolish.Cues, embeddedCues);
            automaticAnalysis = prepared.Analysis;
            if (prepared.Cues is not null)
            {
                status?.Report($"Znaleziono polskie napisy ({downloadedPolish.Provider}) — zapisuję bez tłumaczenia.");
                return await SavePolishAsync(output, prepared.Cues, exportTxt, writeSrt: true, cancellationToken);
            }

            ReportWithheldCandidate(downloadedPolish, automaticAnalysis, status);
        }

        if (fallbackInteraction is not null)
        {
            var reviewCandidate = automaticPolish;
            var reviewAnalysis = automaticAnalysis;
            while (true)
            {
                var choice = await fallbackInteraction.ChooseAsync(
                    new SubtitleFallbackRequest(reviewCandidate, reviewAnalysis,
                        embeddedCues is not null, IsEnglish(selectedTrack),
                        polishDownload.ProviderReportedNoSubtitles), status, cancellationToken)
                    ?? new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel);
                cancellationToken.ThrowIfCancellationRequested();

                switch (choice.Action)
                {
                    case SubtitleFallbackAction.DownloadInteractivePolish:
                        if (choice.InteractivePolish is not { Language: SubtitleLanguage.Polish } interactivePolish)
                        {
                            status?.Report("Interaktywne QNapi nie zwróciło poprawnych polskich napisów.");
                            continue;
                        }
                        try
                        {
                            var preparedInteractive = PreparePolish(interactivePolish.Cues, embeddedCues);
                            if (preparedInteractive.Cues is not null)
                                return await SavePolishAsync(output, preparedInteractive.Cues, exportTxt, writeSrt: true, cancellationToken);
                            reviewCandidate = interactivePolish;
                            reviewAnalysis = preparedInteractive.Analysis;
                            ReportWithheldCandidate(interactivePolish, reviewAnalysis, status);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) when (IsHandledSubtitleException(ex))
                        {
                            status?.Report("Interaktywne QNapi zwróciło nieprawidłowe napisy: " + ex.Message);
                        }
                        continue;

                    case SubtitleFallbackAction.UseLocalPolishSrt:
                        if (string.IsNullOrWhiteSpace(choice.LocalSrtPath) || _cueReader is null)
                        {
                            status?.Report("Wybierz lokalny plik SRT z polskimi napisami.");
                            continue;
                        }
                        try
                        {
                            var localCues = await _cueReader.ReadFileAsync(choice.LocalSrtPath, cancellationToken);
                            var localPolish = new DownloadedSubtitles(SubtitleLanguage.Polish, "lokalny plik", localCues);
                            var preparedLocal = PreparePolish(localCues, embeddedCues);
                            if (preparedLocal.Cues is not null)
                                return await SavePolishAsync(output, preparedLocal.Cues, exportTxt, writeSrt: true, cancellationToken);
                            reviewCandidate = localPolish;
                            reviewAnalysis = preparedLocal.Analysis;
                            ReportWithheldCandidate(localPolish, reviewAnalysis, status);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex) when (IsHandledSubtitleException(ex))
                        {
                            status?.Report("Nie udało się użyć lokalnych napisów: " + ex.Message);
                        }
                        continue;

                    case SubtitleFallbackAction.ApplyRecommendedTransform when reviewCandidate is not null &&
                                                                             reviewAnalysis?.Decision == SubtitleSyncDecision.NeedsReview &&
                                                                             _synchronization is not null:
                        return await SavePolishAsync(output, _synchronization.Apply(reviewCandidate.Cues, reviewAnalysis.Transform),
                            exportTxt, writeSrt: true, cancellationToken);

                    case SubtitleFallbackAction.UseWithoutChanges when reviewCandidate is not null &&
                                                                      reviewAnalysis?.Decision == SubtitleSyncDecision.NeedsReview:
                        return await SavePolishAsync(output, reviewCandidate.Cues, exportTxt, writeSrt: true, cancellationToken);

                    case SubtitleFallbackAction.TranslateEmbeddedEnglish:
                        if (embeddedCues is null)
                            throw new InvalidOperationException("Nie ma wczytanej ścieżki napisów z filmu do tłumaczenia.");
                        status?.Report("Używam wybranej ścieżki z filmu jako źródła tłumaczenia.");
                        return await _translationPipeline.TranslateVideoSubtitlesAsync(inputPath, embeddedCues, provider,
                            exportTxt, translationProgress, status, cancellationToken);

                    case SubtitleFallbackAction.ContinueWithEnglishFallback:
                        goto ContinueWithEnglishFallback;

                    case SubtitleFallbackAction.Cancel:
                        throw new OperationCanceledException("Wybór alternatywnych napisów został anulowany.", cancellationToken);

                    default:
                        throw new InvalidOperationException("Wybrana alternatywa napisów nie jest dostępna dla bieżącego wyniku.");
                }
            }
        }

    ContinueWithEnglishFallback:
        if (embeddedCues is not null && IsEnglish(selectedTrack))
        {
            status?.Report("Używam angielskiej ścieżki z filmu jako źródła tłumaczenia.");
            return await _translationPipeline.TranslateVideoSubtitlesAsync(inputPath, embeddedCues, provider,
                exportTxt, translationProgress, status, cancellationToken);
        }

        var downloadedEnglish = (await TryDownloadAsync(inputPath, SubtitleLanguage.English, failures, status, cancellationToken)).Subtitles;
        if (downloadedEnglish is not null)
        {
            status?.Report($"Znaleziono angielskie napisy ({downloadedEnglish.Provider}) — tłumaczę z zachowaniem audio filmu.");
            return await _translationPipeline.TranslateVideoSubtitlesAsync(inputPath, downloadedEnglish.Cues, provider,
                exportTxt, translationProgress, status, cancellationToken);
        }

        if (selectedTrack is { IsText: true } && _cueReader is null)
        {
            status?.Report("Brak pobranych napisów — używam wybranej ścieżki z filmu.");
            return await _translationPipeline.TranslateAsync(inputPath, selectedTrack, provider, exportTxt,
                translationProgress, status, cancellationToken);
        }

        var reason = failures.Count > 0 ? " Wyszukiwanie napotkało błędy: " + string.Join("; ", failures) : string.Empty;
        throw new InvalidOperationException("Nie znaleziono napisów PL ani EN i film nie ma angielskiej tekstowej ścieżki. " +
            "Wczytaj pasujący plik SRT lub spróbuj ponownie później." + reason);
    }

    private async Task<SubtitleDownloadResult> TryDownloadAsync(
        string inputPath,
        SubtitleLanguage language,
        ICollection<string> failures,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var label = language == SubtitleLanguage.Polish ? "PL" : "EN";
        status?.Report($"QNapi: szukam napisów {label}…");
        try
        {
            SubtitleDownloadResult result;
            if (_downloader is ISubtitleDownloadResultProvider outcomeProvider)
            {
                result = await outcomeProvider.DownloadWithResultAsync(inputPath, language, status, cancellationToken);
            }
            else
            {
                var downloaded = await _downloader.DownloadAsync(inputPath, language, status, cancellationToken);
                result = downloaded is null ? SubtitleDownloadResult.NoSelection() : SubtitleDownloadResult.Found(downloaded);
            }
            var found = result.Subtitles;
            cancellationToken.ThrowIfCancellationRequested();
            if (found is null)
            {
                status?.Report($"QNapi: nie znaleziono napisów {label}.");
                return result;
            }
            if (found.Language != language)
                throw new InvalidDataException($"Źródło zwróciło inny język niż {label}.");
            ValidateCues(found.Cues);
            return result;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (IsHandledSubtitleException(ex))
        {
            failures.Add($"{label}: {ex.Message}");
            status?.Report($"Wyszukiwanie {label} nie powiodło się: {ex.Message}");
            return SubtitleDownloadResult.NoSelection();
        }
    }

    private PreparedPolish PreparePolish(IReadOnlyList<SubtitleCue> cues, IReadOnlyList<SubtitleCue>? reference)
    {
        ValidateCues(cues);
        if (reference is null || _synchronization is null)
            return new PreparedPolish(cues, null);

        var analysis = _synchronization.Analyze(reference, cues);
        return analysis.Decision switch
        {
            SubtitleSyncDecision.Aligned => new PreparedPolish(cues, analysis),
            SubtitleSyncDecision.SafeToSynchronize => new PreparedPolish(_synchronization.Apply(cues, analysis.Transform), analysis),
            _ => new PreparedPolish(null, analysis)
        };
    }

    private static void ReportWithheldCandidate(DownloadedSubtitles candidate, SubtitleTimingAnalysis? analysis, IProgress<string>? status)
    {
        if (analysis is null)
            return;
        status?.Report($"Nie zapisuję napisów {candidate.Provider}: {analysis.Decision}, {candidate.Cues.Count} kwestii, " +
            $"skala {analysis.Transform.Scale:F6}, przesunięcie {analysis.Transform.Offset.TotalMilliseconds:F0} ms, " +
            $"pokrycie {analysis.MatchedCueCoverage:P0}, P90 {analysis.P90Residual.TotalMilliseconds:F0} ms.");
    }

    private async Task<TranslationResult> SavePolishAsync(string output, IReadOnlyList<SubtitleCue> cues,
        bool exportTxt, bool writeSrt, CancellationToken cancellationToken)
    {
        ValidateCues(cues);
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

    private static bool IsEnglish(SubtitleTrack? track) =>
        track?.Language is not null && (track.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                                        track.Language.Equals("eng", StringComparison.OrdinalIgnoreCase));

    private static bool IsHandledSubtitleException(Exception ex) =>
        ex is IOException or InvalidDataException or InvalidOperationException or HttpRequestException or TimeoutException or UnauthorizedAccessException or
            Win32Exception or NotSupportedException;

    private static void ValidateCues(IReadOnlyList<SubtitleCue> cues)
    {
        if (cues.Count == 0 || cues.Any(c => c.Start < TimeSpan.Zero || c.End <= c.Start || string.IsNullOrWhiteSpace(c.Text))
            || cues.Select(c => c.Index).Distinct().Count() != cues.Count)
            throw new InvalidDataException("Plik napisów jest pusty lub ma nieprawidłowe czasy/numery kwestii; istniejący plik nie zostanie nadpisany.");
    }

    private sealed record PreparedPolish(IReadOnlyList<SubtitleCue>? Cues, SubtitleTimingAnalysis? Analysis);
}
