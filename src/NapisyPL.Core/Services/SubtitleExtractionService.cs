using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed class SubtitleExtractionService(FfmpegManager ffmpegManager, ProcessRunner processRunner)
{
    public async Task<string> ExtractToTemporarySrtAsync(string mediaPath, SubtitleTrack track, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (!track.IsText)
            throw new NotSupportedException($"Ścieżka {track.Codec} jest obrazkowa i wymaga OCR, którego MVP nie obsługuje.");

        await ffmpegManager.EnsureAvailableAsync(status, cancellationToken);
        var output = Path.Combine(Path.GetTempPath(), "NapisyPL-" + Guid.NewGuid().ToString("N") + ".srt");
        var result = await processRunner.RunAsync(ffmpegManager.FfmpegPath,
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", mediaPath,
            "-map", $"0:{track.StreamIndex}",
            "-c:s", "srt",
            output
        ], cancellationToken);

        if (result.ExitCode != 0)
        {
            TryDelete(output);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "FFmpeg nie mógł wyciągnąć napisów." : result.StandardError.Trim());
        }
        return output;
    }

    public async Task<string> ConvertSubtitleFileToTemporarySrtAsync(string subtitlePath, IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        await ffmpegManager.EnsureAvailableAsync(status, cancellationToken);
        var output = Path.Combine(Path.GetTempPath(), "NapisyPL-" + Guid.NewGuid().ToString("N") + ".srt");
        var result = await processRunner.RunAsync(ffmpegManager.FfmpegPath,
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", subtitlePath,
            "-c:s", "srt",
            output
        ], cancellationToken);
        if (result.ExitCode != 0)
        {
            TryDelete(output);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StandardError) ? "FFmpeg nie mógł przekonwertować napisów." : result.StandardError.Trim());
        }
        return output;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
