using System.Diagnostics;
using System.Media;
using NapisyPL.Core.Services;

namespace NapisyPL.Review;

/// <summary>
/// Plays the seconds of the film a cue covers, so the question "who says this?" can be answered
/// by ear instead of by hunting through the video. The clip is cut with the FFmpeg the app
/// already carries and played as a plain WAV.
/// </summary>
public sealed class CuePlayer(FfmpegManager ffmpegManager) : IDisposable
{
    /// <summary>A moment before and after, so the line does not start mid-word.</summary>
    private static readonly TimeSpan Padding = TimeSpan.FromMilliseconds(250);

    private readonly string _clipPath = Path.Combine(Path.GetTempPath(), "SubFlow-cue-" + Guid.NewGuid().ToString("N") + ".wav");
    private SoundPlayer? _player;

    public async Task PlayAsync(string videoPath, TimeSpan start, TimeSpan end, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Odtwarzanie fragmentu jest dostępne tylko na Windows.");

        Stop();
        await ffmpegManager.EnsureAvailableAsync(cancellationToken: cancellationToken);

        var from = start - Padding;
        if (from < TimeSpan.Zero)
            from = TimeSpan.Zero;
        var duration = end - start + Padding + Padding;

        var arguments = string.Join(' ',
            "-v", "error", "-y",
            "-ss", from.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            "-t", duration.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture),
            "-i", Quote(videoPath),
            "-vn", "-ac", "1", "-ar", "22050", "-acodec", "pcm_s16le",
            Quote(_clipPath));

        using var process = Process.Start(new ProcessStartInfo(ffmpegManager.FfmpegPath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("Nie udało się uruchomić FFmpeg.");

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0 || !File.Exists(_clipPath))
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "FFmpeg nie wyciął fragmentu." : error.Trim());

        _player = new SoundPlayer(_clipPath);
        _player.Play();
    }

    public void Stop()
    {
        if (!OperatingSystem.IsWindows())
            return;

        _player?.Stop();
        _player?.Dispose();
        _player = null;
    }

    public void Dispose()
    {
        Stop();
        try
        {
            if (File.Exists(_clipPath))
                File.Delete(_clipPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Quote(string path) => "\"" + path + "\"";
}
