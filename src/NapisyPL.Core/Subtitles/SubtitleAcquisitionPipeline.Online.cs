using NapisyPL.Core.Models;
using NapisyPL.Core.Subtitles.Online;

namespace NapisyPL.Core.Subtitles;

/// <summary>When people speak in the video; null when it cannot be measured.</summary>
public delegate Task<IReadOnlyList<SpeechSpan>?> SpeechTimelineProvider(
    string videoPath,
    IProgress<string>? status,
    CancellationToken cancellationToken);

public sealed partial class SubtitleAcquisitionPipeline
{
    /// <summary>Downloads count against daily limits, so only the best few candidates are tried.</summary>
    private const int MaximumOnlineDownloads = 3;

    /// <summary>SubDL and OpenSubtitles.com, present only when the user gave a key.</summary>
    public IReadOnlyList<IOnlineSubtitleSource> OnlineSources { get; set; } = [];

    /// <summary>The last-resort clock for subtitles of another release: the video's own speech.</summary>
    public SpeechTimelineProvider? SpeechTimeline { get; set; }

    private sealed record OnlinePolish(IReadOnlyList<SubtitleCue> Cues, string Provider, string Detail);

    private sealed record PreparedAgainstVideo(IReadOnlyList<SubtitleCue>? Cues, string Clock, double Coverage, int Segments, int Dropped);

    /// <summary>
    /// Everything that can tell the time of this video, fetched once and only when needed:
    /// embedded subtitles, English subtitles made for this exact file, and the audio.
    /// </summary>
    private sealed class VideoClock(
        SubtitleAcquisitionPipeline pipeline,
        string videoPath,
        IReadOnlyList<SubtitleCue>? embeddedCues,
        ICollection<string> failures,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        private bool _englishResolved;
        private DownloadedSubtitles? _english;
        private bool _speechResolved;
        private IReadOnlyList<SpeechSpan>? _speech;
        private OnlineSubtitleQuery? _query;

        public IReadOnlyList<SubtitleCue>? Embedded => embeddedCues;

        /// <summary>English for this exact file: QNapi, then an OpenSubtitles hash match.</summary>
        public async Task<DownloadedSubtitles?> GetEnglishAsync()
        {
            if (_englishResolved)
                return _english;
            _englishResolved = true;

            _english = (await pipeline.TryDownloadAsync(videoPath, SubtitleLanguage.English, failures, status, cancellationToken)).Subtitles;
            if (_english is not null || pipeline.OnlineSources.Count == 0)
                return _english;

            var query = await GetQueryAsync();
            if (query.MovieHash is null)
                return null;

            foreach (var source in pipeline.OnlineSources)
            {
                try
                {
                    var match = (await source.SearchAsync(query, SubtitleLanguage.English, cancellationToken))
                        .Where(candidate => candidate.IsHashMatch)
                        .OrderBy(candidate => candidate.HearingImpaired)
                        .ThenByDescending(candidate => candidate.DownloadCount)
                        .FirstOrDefault();
                    if (match is null)
                        continue;
                    status?.Report($"{source.Name}: pobieram angielskie napisy do tego pliku…");
                    var cues = await source.DownloadAsync(match, query, cancellationToken);
                    ValidateCues(cues);
                    _english = new DownloadedSubtitles(SubtitleLanguage.English, source.Name, cues);
                    return _english;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) when (IsOnlineException(exception))
                {
                    failures.Add($"{source.Name} EN: {exception.Message}");
                    status?.Report($"{source.Name}: {exception.Message}");
                }
            }

            return null;
        }

        /// <summary>Any subtitles timed to this video, whatever their language.</summary>
        public async Task<IReadOnlyList<SubtitleCue>?> GetTimingReferenceAsync() =>
            embeddedCues ?? (await GetEnglishAsync())?.Cues;

        public async Task<IReadOnlyList<SpeechSpan>?> GetSpeechAsync()
        {
            if (_speechResolved)
                return _speech;
            _speechResolved = true;
            if (pipeline.SpeechTimeline is null)
                return null;

            try
            {
                status?.Report("Nie ma napisów do porównania — sprawdzam, kiedy w filmie ktoś mówi…");
                _speech = await pipeline.SpeechTimeline(videoPath, status, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or DllNotFoundException or BadImageFormatException or HttpRequestException)
            {
                failures.Add("speech: " + exception.Message);
                _speech = null;
            }
            return _speech;
        }

        public async Task<OnlineSubtitleQuery> GetQueryAsync() =>
            _query ??= new OnlineSubtitleQuery(videoPath, ReleaseName.Parse(videoPath),
                await MovieHash.ComputeAsync(videoPath, cancellationToken));
    }

    /// <summary>
    /// Times Polish subtitles to this video: against subtitles of this release when there are
    /// any, otherwise against the video's speech. Null cues mean the fit is not safe.
    /// </summary>
    private async Task<PreparedAgainstVideo> PrepareAgainstVideoAsync(IReadOnlyList<SubtitleCue> polish, VideoClock clock)
    {
        ValidateCues(polish);
        var reference = await clock.GetTimingReferenceAsync();
        if (reference is not null)
        {
            var prepared = PreparePolish(polish, reference);
            return new PreparedAgainstVideo(prepared.Cues, "napisy",
                prepared.Piecewise?.MatchedCueCoverage ?? prepared.Analysis?.MatchedCueCoverage ?? 1,
                prepared.Piecewise?.Segments.Count ?? 1,
                prepared.Piecewise?.DroppedCues ?? 0);
        }

        var speech = await clock.GetSpeechAsync();
        if (speech is null || speech.Count == 0 || _synchronization is null)
            return new PreparedAgainstVideo(null, "brak", 0, 0, 0);

        var fit = _synchronization.AnalyzeAgainstSpeech(speech, polish);
        return new PreparedAgainstVideo(
            fit.Decision is SubtitleSyncDecision.SafeToSynchronize or SubtitleSyncDecision.Aligned ? fit.Cues : null,
            "mowa", fit.MatchedCueCoverage, fit.Segments.Count, fit.DroppedCues);
    }

    /// <summary>
    /// Polish from SubDL / OpenSubtitles. A file made for this exact video is used like QNapi's;
    /// any other release is kept only when it fits the video's clock.
    /// </summary>
    private async Task<OnlinePolish?> TryOnlinePolishAsync(
        string videoPath,
        VideoClock clock,
        ICollection<string> failures,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (OnlineSources.Count == 0)
            return null;

        var query = await clock.GetQueryAsync();
        var found = new List<OnlineSubtitleCandidate>();
        foreach (var source in OnlineSources)
        {
            try
            {
                status?.Report($"{source.Name}: szukam polskich napisów…");
                found.AddRange(await source.SearchAsync(query, SubtitleLanguage.Polish, cancellationToken));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (IsOnlineException(exception))
            {
                failures.Add($"{source.Name} PL: {exception.Message}");
                status?.Report($"{source.Name}: {exception.Message}");
            }
        }

        var ranked = OnlineSubtitleRanking.Rank(found, query.Release);
        if (ranked.Count == 0)
        {
            status?.Report("SubDL/OpenSubtitles: brak polskich napisów do tego odcinka.");
            return null;
        }

        var downloads = 0;
        foreach (var candidate in ranked)
        {
            if (downloads == MaximumOnlineDownloads)
                break;

            // Another release is only worth a download when something can check its timing.
            if (!candidate.IsHashMatch &&
                await clock.GetTimingReferenceAsync() is null &&
                (await clock.GetSpeechAsync() is not { Count: > 0 }))
            {
                status?.Report("Znalazłem polskie napisy do innego wydania, ale nie mam czym sprawdzić ich czasu.");
                return null;
            }

            IReadOnlyList<SubtitleCue> cues;
            try
            {
                downloads++;
                status?.Report($"{candidate.Source}: pobieram „{candidate.ReleaseName}”…");
                var source = OnlineSources.First(item => item.Name == candidate.Source);
                cues = await source.DownloadAsync(candidate, query, cancellationToken);
                ValidateCues(cues);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (IsOnlineException(exception))
            {
                failures.Add($"{candidate.Source} PL: {exception.Message}");
                status?.Report($"{candidate.Source}: {exception.Message}");
                continue;
            }

            if (candidate.IsHashMatch && await clock.GetTimingReferenceAsync() is null)
                return new OnlinePolish(cues, candidate.Source, $"Pobrano polskie napisy do tego pliku ({candidate.Source}).");

            var prepared = await PrepareAgainstVideoAsync(cues, clock);
            if (prepared.Cues is not null)
            {
                var detail = $"Pobrano polskie napisy ({candidate.Source}, „{candidate.ReleaseName}”) i dopasowano czas " +
                             (prepared.Clock == "mowa" ? "do mowy w filmie" : "do napisów tego wydania") +
                             $": {prepared.Coverage:P0} kwestii" +
                             (prepared.Segments > 1 ? $", {prepared.Segments} odcinki czasu." : ".");
                return new OnlinePolish(prepared.Cues, candidate.Source, detail);
            }

            status?.Report($"{candidate.Source}: „{candidate.ReleaseName}” nie pasuje czasem ({prepared.Coverage:P0}).");
        }

        return null;
    }

    private static bool IsOnlineException(Exception exception) =>
        exception is HttpRequestException or IOException or InvalidDataException or System.Text.Json.JsonException or
            FormatException or TaskCanceledException { InnerException: TimeoutException };
}
