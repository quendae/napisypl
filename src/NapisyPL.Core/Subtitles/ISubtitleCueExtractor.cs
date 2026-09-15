using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

/// <summary>Extracts one textual subtitle track to an owned temporary SRT file.</summary>
public interface ISubtitleCueExtractor
{
    Task ExtractToSrtAsync(
        string mediaPath,
        SubtitleTrack track,
        string outputPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}
