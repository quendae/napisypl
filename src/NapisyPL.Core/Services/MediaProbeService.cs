using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed class MediaProbeService(FfmpegManager ffmpegManager, ProcessRunner processRunner)
{
    public async Task<IReadOnlyList<SubtitleTrack>> ProbeAsync(string mediaPath, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        await ffmpegManager.EnsureAvailableAsync(status, cancellationToken);
        var result = await processRunner.RunAsync(ffmpegManager.FfprobePath,
        [
            "-v", "error",
            "-select_streams", "s",
            "-show_entries", "stream=index,codec_name:stream_tags=language,title",
            "-of", "json",
            mediaPath
        ], cancellationToken);

        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "ffprobe nie mógł odczytać pliku." : result.StandardError.Trim());

        return FfprobeParser.Parse(result.StandardOutput);
    }
}
