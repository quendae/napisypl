using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

public enum SubtitleLanguage { Polish, English }

public sealed record DownloadedSubtitles(
    SubtitleLanguage Language,
    string Provider,
    IReadOnlyList<SubtitleCue> Cues);

public interface ISubtitleDownloader
{
    Task<DownloadedSubtitles?> DownloadAsync(
        string videoPath,
        SubtitleLanguage language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Opens a provider-owned selector for a single video and returns its validated subtitle result.</summary>
public interface IInteractiveSubtitleDownloader
{
    Task<DownloadedSubtitles?> DownloadInteractiveAsync(
        string videoPath,
        SubtitleLanguage language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}
