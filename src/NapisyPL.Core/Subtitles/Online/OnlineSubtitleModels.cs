using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles.Online;

/// <summary>What we know about a video from its file: name, episode and the OpenSubtitles hash.</summary>
public sealed record OnlineSubtitleQuery(
    string VideoPath,
    ReleaseName Release,
    string? MovieHash);

/// <summary>One subtitle file offered by a source. <see cref="Token"/> is whatever the source needs to download it.</summary>
public sealed record OnlineSubtitleCandidate(
    string Source,
    SubtitleLanguage Language,
    string ReleaseName,
    bool IsHashMatch,
    bool HearingImpaired,
    int DownloadCount,
    string Token)
{
    /// <summary>Filled by <see cref="OnlineSubtitleRanking"/>.</summary>
    public double ReleaseSimilarity { get; init; }
}

public interface IOnlineSubtitleSource
{
    string Name { get; }

    Task<IReadOnlyList<OnlineSubtitleCandidate>> SearchAsync(
        OnlineSubtitleQuery query,
        SubtitleLanguage language,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubtitleCue>> DownloadAsync(
        OnlineSubtitleCandidate candidate,
        OnlineSubtitleQuery query,
        CancellationToken cancellationToken = default);
}

/// <summary>A key was rejected, a daily limit hit, or the service is down: worth telling the user.</summary>
public sealed class OnlineSubtitleSourceException(string message, Exception? inner = null) : IOException(message, inner);

public static class OnlineSubtitleRanking
{
    /// <summary>Hash matches first, then the release closest to the video's file name, then popularity.</summary>
    public static IReadOnlyList<OnlineSubtitleCandidate> Rank(
        IEnumerable<OnlineSubtitleCandidate> candidates,
        ReleaseName video)
    {
        return candidates
            .Select(candidate => candidate with { ReleaseSimilarity = video.Similarity(ReleaseName.Parse(candidate.ReleaseName)) })
            .Where(candidate => candidate.IsHashMatch || IsSameTitleAndEpisode(video, ReleaseName.Parse(candidate.ReleaseName)))
            .OrderByDescending(candidate => candidate.IsHashMatch)
            .ThenByDescending(candidate => candidate.ReleaseSimilarity)
            .ThenBy(candidate => candidate.HearingImpaired)
            .ThenByDescending(candidate => candidate.DownloadCount)
            .ToArray();
    }

    /// <summary>
    /// Text search returns look-alikes ("Extraordinary You", "Love by Chance" for "Chance"),
    /// so a release that is not a hash match must carry the same title and episode.
    /// </summary>
    public static bool IsSameTitleAndEpisode(ReleaseName video, ReleaseName candidate) =>
        video.SameEpisodeAs(candidate) &&
        video.IsEpisode == candidate.IsEpisode &&
        ReleaseName.NormalizeTitle(candidate.Title) is { Length: > 0 } title &&
        title == ReleaseName.NormalizeTitle(video.Title);
}
