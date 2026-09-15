using System.ComponentModel;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

/// <summary>Reads validated subtitle cues without changing the source media or subtitle file.</summary>
public interface ISubtitleCueReader
{
    Task<IReadOnlyList<SubtitleCue>> ReadEmbeddedAsync(
        string videoPath,
        SubtitleTrack track,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SubtitleCue>> ReadFileAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default);
}
