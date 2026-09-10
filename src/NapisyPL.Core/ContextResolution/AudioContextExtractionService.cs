using NapisyPL.Core.Services;

namespace NapisyPL.Core.ContextResolution;

public sealed class AudioContextExtractionService(
    FfmpegManager ffmpegManager,
    ProcessRunner processRunner)
{
    public async Task<string> ExtractTemporaryMono16KhzWaveAsync(
        string mediaPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        await ffmpegManager.EnsureAvailableAsync(status, cancellationToken);

        var outputPath = Path.Combine(Path.GetTempPath(), "SubFlow-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        status?.Report("Enhanced: przygotowuję lekką ścieżkę audio 16 kHz…");

        var result = await processRunner.RunAsync(ffmpegManager.FfmpegPath,
        [
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", mediaPath,
            "-map", "0:a:0",
            "-vn",
            "-ac", "1",
            "-ar", "16000",
            "-c:a", "pcm_s16le",
            outputPath
        ], cancellationToken);

        if (result.ExitCode != 0 || !File.Exists(outputPath))
        {
            try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "FFmpeg nie utworzył ścieżki audio."
                : result.StandardError.Trim();
            throw new InvalidOperationException("Enhanced: nie udało się wyciągnąć audio. " + detail);
        }

        return outputPath;
    }
}
