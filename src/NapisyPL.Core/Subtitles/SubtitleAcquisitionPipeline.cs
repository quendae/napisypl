using System.ComponentModel;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Subtitles;

/// <summary>Resolves Polish subtitles before invoking any translation provider.</summary>
public sealed partial class SubtitleAcquisitionPipeline : ITranslationPipeline
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

        var clock = new VideoClock(this, inputPath, embeddedCues, failures, status, cancellationToken);
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

        var online = await TryOnlinePolishAsync(inputPath, clock, failures, status, cancellationToken);
        if (online is not null)
        {
            status?.Report(online.Detail);
            return await SavePolishAsync(output, online.Cues, exportTxt, writeSrt: true, cancellationToken);
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
                            var preparedLocal = await PrepareAgainstVideoAsync(localCues, clock);
                            if (preparedLocal.Cues is not null)
                                return await SavePolishAsync(output, preparedLocal.Cues, exportTxt, writeSrt: true, cancellationToken);
                            reviewCandidate = localPolish;
                            reviewAnalysis = await clock.GetTimingReferenceAsync() is { } localReference && _synchronization is not null
                                ? _synchronization.Analyze(localReference, localCues)
                                : null;
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

        var downloadedEnglish = await clock.GetEnglishAsync();
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

    /// <summary>
    /// The automatic search behind the Explorer context menu, without translating:
    /// keep an existing <c>.pl.srt</c> or download Polish from QNapi; when there is
    /// none, look for English first inside the video and then on QNapi. The English
    /// cues are returned so machine translation can start later without searching again.
    /// </summary>
    public async Task<SubtitleAvailability> ScanAsync(
        string videoPath,
        SubtitleTrack? englishTrack,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        cancellationToken.ThrowIfCancellationRequested();

        var failures = new List<string>();
        var output = Path.Combine(Path.GetDirectoryName(videoPath) ?? Environment.CurrentDirectory,
            Path.GetFileNameWithoutExtension(videoPath) + ".pl.srt");

        if (File.Exists(output))
        {
            status?.Report("Polskie napisy już są obok filmu.");
            return new SubtitleAvailability(videoPath, PolishSubtitleState.Existing, null, output,
                EnglishSubtitleSource.None, null, null, failures);
        }

        IReadOnlyList<SubtitleCue>? embeddedCues = null;
        if (englishTrack is { IsText: true } && IsEnglish(englishTrack) && _cueReader is not null)
        {
            try
            {
                embeddedCues = await _cueReader.ReadEmbeddedAsync(videoPath, englishTrack, status, cancellationToken);
                ValidateCues(embeddedCues);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsHandledSubtitleException(ex))
            {
                embeddedCues = null;
                failures.Add("embedded: " + ex.Message);
            }
        }

        var clock = new VideoClock(this, videoPath, embeddedCues, failures, status, cancellationToken);
        var polishState = PolishSubtitleState.Missing;
        string? polishProvider = null;
        var polish = (await TryDownloadAsync(videoPath, SubtitleLanguage.Polish, failures, status, cancellationToken)).Subtitles;
        if (polish is not null)
        {
            polishProvider = polish.Provider;
            var prepared = PreparePolish(polish.Cues, embeddedCues);
            if (prepared.Cues is not null)
            {
                await SavePolishAsync(output, prepared.Cues, exportTxt: false, writeSrt: true, cancellationToken);
                status?.Report($"Zapisano polskie napisy ({polish.Provider}).");
                return new SubtitleAvailability(videoPath, PolishSubtitleState.Saved, polish.Provider, output,
                    embeddedCues is null ? EnglishSubtitleSource.None : EnglishSubtitleSource.Embedded,
                    null, embeddedCues, failures);
            }

            polishState = PolishSubtitleState.NeedsReview;
            ReportWithheldCandidate(polish, prepared.Analysis, status);
        }

        var online = await TryOnlinePolishAsync(videoPath, clock, failures, status, cancellationToken);
        if (online is not null)
        {
            await SavePolishAsync(output, online.Cues, exportTxt: false, writeSrt: true, cancellationToken);
            status?.Report(online.Detail);
            return new SubtitleAvailability(videoPath, PolishSubtitleState.Saved, online.Provider, output,
                embeddedCues is null ? EnglishSubtitleSource.None : EnglishSubtitleSource.Embedded,
                null, embeddedCues, failures);
        }

        if (embeddedCues is not null)
        {
            return new SubtitleAvailability(videoPath, polishState, polishProvider, null,
                EnglishSubtitleSource.Embedded, null, embeddedCues, failures);
        }

        var english = await clock.GetEnglishAsync();
        return english is null
            ? new SubtitleAvailability(videoPath, polishState, polishProvider, null,
                EnglishSubtitleSource.None, null, null, failures)
            : new SubtitleAvailability(videoPath, polishState, polishProvider, null,
                EnglishSubtitleSource.Downloaded, english.Provider, english.Cues, failures);
    }

    /// <summary>Machine-translates the English source a scan found.</summary>
    public Task<TranslationResult> TranslateScannedAsync(
        SubtitleAvailability availability,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(availability);
        ArgumentNullException.ThrowIfNull(provider);
        if (!availability.CanTranslate)
            throw new InvalidOperationException("Dla tego filmu nie znaleziono angielskich napisów do tłumaczenia.");

        return _translationPipeline.TranslateVideoSubtitlesAsync(availability.VideoPath, availability.EnglishCues!,
            provider, exportTxt, translationProgress, status, cancellationToken);
    }

    /// <summary>
    /// Fits Polish subtitles made for another release of this video onto it, using English
    /// subtitles that match the video (the scan's, else embedded, else QNapi) as the clock.
    /// Saves <c>.pl.srt</c> only when the fit is safe; never overwrites an existing file.
    /// </summary>
    public async Task<PolishSyncOutcome> SynchronizeOtherReleaseAsync(
        string videoPath,
        string polishSubtitlePath,
        IReadOnlyList<SubtitleCue>? englishReference,
        SubtitleTrack? englishTrack,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(polishSubtitlePath);
        if (_cueReader is null || _synchronization is null)
            throw new InvalidOperationException("Synchronizacja napisów nie jest dostępna.");

        var output = Path.Combine(Path.GetDirectoryName(videoPath) ?? Environment.CurrentDirectory,
            Path.GetFileNameWithoutExtension(videoPath) + ".pl.srt");
        if (File.Exists(output))
            return new PolishSyncOutcome(SubtitleSyncDecision.Rejected, null, "Obok filmu są już polskie napisy — nie nadpisuję ich.");

        status?.Report("Wczytuję polskie napisy…");
        var polish = await _cueReader.ReadFileAsync(polishSubtitlePath, cancellationToken);
        ValidateCues(polish);

        var failures = new List<string>();
        var reference = englishReference;
        if (reference is null && englishTrack is { IsText: true } && IsEnglish(englishTrack))
        {
            try
            {
                status?.Report("Odczytuję angielskie napisy z filmu…");
                reference = await _cueReader.ReadEmbeddedAsync(videoPath, englishTrack, status, cancellationToken);
                ValidateCues(reference);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsHandledSubtitleException(ex))
            {
                reference = null;
                failures.Add("embedded: " + ex.Message);
            }
        }
        var clock = new VideoClock(this, videoPath, reference, failures, status, cancellationToken);
        status?.Report("Dopasowuję czas do filmu…");
        var prepared = await PrepareAgainstVideoAsync(polish, clock);
        if (prepared.Cues is null)
        {
            return new PolishSyncOutcome(SubtitleSyncDecision.Rejected, null, prepared.Clock == "brak"
                ? "Nie ma angielskich napisów tego wydania ani czytelnej mowy w audio, więc nie ma do czego dopasować czasu."
                : $"Nie zapisano: te napisy pasują tylko w {prepared.Coverage:P0}. To może być inny odcinek albo zupełnie inaczej podzielony tekst.");
        }

        await SavePolishAsync(output, prepared.Cues, exportTxt: false, writeSrt: true, cancellationToken);
        var detail = $"Zapisano {Path.GetFileName(output)} · dopasowano {prepared.Coverage:P0} kwestii " +
                     (prepared.Clock == "mowa" ? "do mowy w filmie" : "do napisów tego wydania") +
                     (prepared.Segments > 1 ? $" w {prepared.Segments} odcinkach czasu" : string.Empty) +
                     (prepared.Dropped > 0 ? $", pominięto {prepared.Dropped} spoza filmu." : ".");
        return new PolishSyncOutcome(SubtitleSyncDecision.SafeToSynchronize, output, detail);
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
        switch (analysis.Decision)
        {
            case SubtitleSyncDecision.Aligned:
                return new PreparedPolish(cues, analysis);
            case SubtitleSyncDecision.SafeToSynchronize:
                return new PreparedPolish(_synchronization.Apply(cues, analysis.Transform), analysis);
        }

        // Another release of the same episode: one shift does not fit, but stretches do.
        var piecewise = _synchronization.AnalyzePiecewise(reference, cues);
        return piecewise.Decision is SubtitleSyncDecision.Aligned or SubtitleSyncDecision.SafeToSynchronize
            ? new PreparedPolish(piecewise.Cues, analysis, piecewise)
            : new PreparedPolish(null, analysis, piecewise);
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

    private sealed record PreparedPolish(
        IReadOnlyList<SubtitleCue>? Cues,
        SubtitleTimingAnalysis? Analysis,
        PiecewiseSubtitleSync? Piecewise = null);
}
