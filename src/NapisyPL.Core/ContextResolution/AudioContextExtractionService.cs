using NapisyPL.Core.Services;

namespace NapisyPL.Core.ContextResolution;

public enum AudioChannelMode
{
    /// <summary>Average every channel into mono.</summary>
    Downmix,

    /// <summary>Take only the front centre channel, where film dialogue is mixed.</summary>
    Centre
}

/// <summary>
/// Chooses how a multichannel track becomes the mono signal used for diarization
/// and pitch. Films mix dialogue into the front centre channel; averaging all six
/// channels of a 5.1 track blends it with the music and effects from the others.
/// </summary>
public static class AudioChannelPlan
{
    // FFmpeg layout names whose channel list includes FC.
    private static readonly HashSet<string> LayoutsWithCentre = new(StringComparer.OrdinalIgnoreCase)
    {
        "3.0", "3.1", "4.0", "4.1", "5.0", "5.0(side)", "5.1", "5.1(side)",
        "6.0", "6.1", "6.1(back)", "7.0", "7.0(front)", "7.1", "7.1(wide)", "7.1(wide-side)",
        "5.1.2", "5.1.4", "7.1.2", "7.1.4", "hexagonal", "octagonal", "hexadecagonal"
    };

    public static AudioChannelMode Choose(string? channelLayout)
    {
        if (string.IsNullOrWhiteSpace(channelLayout))
            return AudioChannelMode.Downmix;

        var layout = channelLayout.Trim();
        if (LayoutsWithCentre.Contains(layout))
            return AudioChannelMode.Centre;

        // Explicit layouts are printed as channel names joined with '+'.
        return layout.Split('+').Any(name => string.Equals(name.Trim(), "FC", StringComparison.OrdinalIgnoreCase))
            ? AudioChannelMode.Centre
            : AudioChannelMode.Downmix;
    }

    public static IReadOnlyList<string> FilterArguments(AudioChannelMode mode) => mode switch
    {
        AudioChannelMode.Centre => ["-af", "pan=mono|c0=FC"],
        _ => ["-ac", "1"]
    };

    /// <summary>Reads channel_layout from `ffprobe -show_entries stream=channel_layout` output.</summary>
    public static string? ParseProbedLayout(string ffprobeOutput)
    {
        foreach (var raw in ffprobeOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("channel_layout=", StringComparison.OrdinalIgnoreCase))
                return line["channel_layout=".Length..].Trim();
        }

        return null;
    }
}

public sealed class AudioContextExtractionService(
    FfmpegManager ffmpegManager,
    ProcessRunner processRunner)
{
    /// <summary>Set to <see cref="AudioChannelMode.Downmix"/> to disable centre extraction.</summary>
    public AudioChannelMode PreferredMode { get; init; } = AudioChannelMode.Centre;

    public async Task<string> ExtractTemporaryMono16KhzWaveAsync(
        string mediaPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        await ffmpegManager.EnsureAvailableAsync(status, cancellationToken);

        var outputPath = Path.Combine(Path.GetTempPath(), "SubFlow-audio-" + Guid.NewGuid().ToString("N") + ".wav");
        status?.Report("Enhanced: przygotowuję lekką ścieżkę audio 16 kHz…");

        var mode = PreferredMode == AudioChannelMode.Centre
            ? AudioChannelPlan.Choose(await ProbeLayoutAsync(mediaPath, cancellationToken))
            : AudioChannelMode.Downmix;

        var result = await RunExtractionAsync(mediaPath, outputPath, mode, cancellationToken);

        // A layout can claim a centre channel the stream does not actually expose to
        // the pan filter. Falling back keeps Enhanced working on unusual files.
        if (mode == AudioChannelMode.Centre && (result.ExitCode != 0 || !File.Exists(outputPath)))
        {
            TryDelete(outputPath);
            result = await RunExtractionAsync(mediaPath, outputPath, AudioChannelMode.Downmix, cancellationToken);
        }

        if (result.ExitCode != 0 || !File.Exists(outputPath))
        {
            TryDelete(outputPath);
            var detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? "FFmpeg nie utworzył ścieżki audio."
                : result.StandardError.Trim();
            throw new InvalidOperationException("Enhanced: nie udało się wyciągnąć audio. " + detail);
        }

        return outputPath;
    }

    private async Task<string?> ProbeLayoutAsync(string mediaPath, CancellationToken cancellationToken)
    {
        try
        {
            var probe = await processRunner.RunAsync(ffmpegManager.FfprobePath,
            [
                "-v", "error",
                "-select_streams", "a:0",
                "-show_entries", "stream=channel_layout",
                "-of", "default=noprint_wrappers=1",
                mediaPath
            ], cancellationToken);
            return probe.ExitCode == 0 ? AudioChannelPlan.ParseProbedLayout(probe.StandardOutput) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    private Task<ProcessResult> RunExtractionAsync(
        string mediaPath,
        string outputPath,
        AudioChannelMode mode,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-y",
            "-i", mediaPath,
            "-map", "0:a:0",
            "-vn"
        };
        arguments.AddRange(AudioChannelPlan.FilterArguments(mode));
        arguments.AddRange(["-ar", "16000", "-c:a", "pcm_s16le", outputPath]);
        return processRunner.RunAsync(ffmpegManager.FfmpegPath, arguments, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
