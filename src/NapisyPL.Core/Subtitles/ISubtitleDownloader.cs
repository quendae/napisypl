using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

public enum SubtitleLanguage { Polish, English }

public sealed record DownloadedSubtitles(
    SubtitleLanguage Language,
    string Provider,
    IReadOnlyList<SubtitleCue> Cues);

/// <summary>Distinguishes a provider's confirmed miss from closing a selection without saving a file.</summary>
public sealed record SubtitleDownloadResult(
    DownloadedSubtitles? Subtitles,
    bool ProviderReportedNoSubtitles)
{
    public static SubtitleDownloadResult Found(DownloadedSubtitles subtitles) => new(subtitles, false);
    public static SubtitleDownloadResult NoSubtitlesFound() => new(null, true);
    public static SubtitleDownloadResult NoSelection() => new(null, false);
}

public interface ISubtitleDownloader
{
    Task<DownloadedSubtitles?> DownloadAsync(
        string videoPath,
        SubtitleLanguage language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides the provider outcome while retaining the simple downloader contract for existing callers.</summary>
public interface ISubtitleDownloadResultProvider
{
    Task<SubtitleDownloadResult> DownloadWithResultAsync(
        string videoPath,
        SubtitleLanguage language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Opens a provider-owned selector for a single video and returns its validated subtitle result.</summary>
public interface IInteractiveSubtitleDownloader
{
    Task<SubtitleDownloadResult> DownloadInteractiveAsync(
        string videoPath,
        SubtitleLanguage language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}
