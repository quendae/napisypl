using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public interface IMediaProbeService
{
    Task<IReadOnlyList<SubtitleTrack>> ProbeAsync(
        string mediaPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}
