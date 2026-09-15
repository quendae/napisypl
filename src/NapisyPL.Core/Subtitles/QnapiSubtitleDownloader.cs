using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Subtitles;

public sealed record QnapiProcessResult(int ExitCode, string StandardOutput, string StandardError);

public delegate Task<QnapiProcessResult> QnapiProcessInvoker(
    ProcessStartInfo startInfo,
    CancellationToken cancellationToken);

public sealed class QnapiSubtitleDownloader : ISubtitleDownloader
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly IQnapiRuntime _runtime;
    private readonly SrtParser _parser;
    private readonly QnapiProcessInvoker _processInvoker;
    private readonly TimeSpan _processTimeout;

    public QnapiSubtitleDownloader(
        IQnapiRuntime runtime,
        SrtParser parser,
        QnapiProcessInvoker? processInvoker = null,
        TimeSpan? processTimeout = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _processInvoker = processInvoker ?? RunProcessAsync;
        _processTimeout = processTimeout ?? TimeSpan.FromSeconds(90);
        if (_processTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(processTimeout));
    }

    public async Task<DownloadedSubtitles?> DownloadAsync(
        string videoPath,
        SubtitleLanguage language,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        var absoluteVideoPath = Path.GetFullPath(videoPath);
        if (!File.Exists(absoluteVideoPath))
            throw new FileNotFoundException("Nie znaleziono pliku filmu.", absoluteVideoPath);

        var executable = await _runtime.EnsureAvailableAsync(status, cancellationToken);
        var extension = "npl" + Guid.NewGuid().ToString("N");
        var outputPath = Path.ChangeExtension(absoluteVideoPath, extension);
        var languageCode = language == SubtitleLanguage.Polish ? "pl" : "en";

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[]
                 {
                     "-q", "-d", "-l", languageCode, "-lb", languageCode,
                     "-f", "SRT", "-e", extension, absoluteVideoPath
                 })
            startInfo.ArgumentList.Add(argument);

        status?.Report(language == SubtitleLanguage.Polish
            ? "Szukam polskich napisów w QNapi…"
            : "Szukam angielskich napisów w QNapi…");

        using var timeout = new CancellationTokenSource(_processTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            QnapiProcessResult processResult;
            try
            {
                processResult = await _processInvoker(startInfo, linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
            {
                throw new TimeoutException("QNapi nie zakończył wyszukiwania w wyznaczonym czasie.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(outputPath))
            {
                if (processResult.ExitCode != 0)
                {
                    var detail = FirstUsefulLine(processResult.StandardError, processResult.StandardOutput);
                    throw new IOException(string.IsNullOrEmpty(detail)
                        ? $"QNapi zakończył pracę z kodem {processResult.ExitCode}."
                        : $"QNapi zakończył pracę z kodem {processResult.ExitCode}: {detail}");
                }

                return null;
            }

            var content = await File.ReadAllTextAsync(outputPath, Utf8WithoutBom, cancellationToken);
            var cues = _parser.Parse(content);
            if (cues.Count == 0 || cues.Any(cue => cue.Start < TimeSpan.Zero || cue.End <= cue.Start))
                throw new InvalidDataException("QNapi zwrócił nieprawidłowy plik napisów.");

            return new DownloadedSubtitles(language, "QNapi", cues);
        }
        finally
        {
            try
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }
            catch
            {
                // This is a unique owned sidecar. Failure to clean it must not hide the real result.
            }
        }
    }

    private static async Task<QnapiProcessResult> RunProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new IOException("Nie udało się uruchomić QNapi.");
        }
        catch (Win32Exception ex)
        {
            throw new IOException("Nie udało się uruchomić QNapi.", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new QnapiProcessResult(process.ExitCode, await stdout, await stderr);
        }
        catch
        {
            TryKill(process);
            try { await process.WaitForExitAsync(CancellationToken.None); } catch { }
            throw;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Cancellation/timeout remains the actionable error.
        }
    }

    private static string FirstUsefulLine(params string[] values)
    {
        foreach (var value in values)
        {
            var line = value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .FirstOrDefault(item => item.Length > 0);
            if (line is not null)
                return line.Length <= 300 ? line : line[..300];
        }
        return string.Empty;
    }
}
