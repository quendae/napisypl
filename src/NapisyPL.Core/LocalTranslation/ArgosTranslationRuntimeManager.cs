using System.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.LocalTranslation;

public sealed class ArgosTranslationRuntimeManager(
    ArgosModelAssetManager assetManager,
    ArgosRuntimeOptions options) : IArgosTranslatorClient, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Process? _process;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _stderrDrain;
    private bool _loaded;

    public bool IsRunning => _process is { HasExited: false };
    public IProgress<string>? StatusProgress { get; set; }

    public async Task EnsureReadyAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureReadyCoreAsync(status ?? StatusProgress, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
            return [];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureReadyCoreAsync(StatusProgress, cancellationToken);
            var process = _process ?? throw new InvalidOperationException("Argos helper is not running.");
            var jobId = Guid.NewGuid().ToString("N");
            var segments = texts.Select((text, index) => new TranslationSegment(index + 1, text)).ToArray();
            await SendCommandAsync(process, new TranslateCommand(jobId, segments), cancellationToken);

            var result = new string?[texts.Count];
            while (true)
            {
                var @event = await ReadEventAsync(process, cancellationToken);
                switch (@event)
                {
                    case SegmentEvent segment when segment.JobId == jobId:
                        if (segment.Id <= 0 || segment.Id > result.Length)
                            throw new InvalidDataException($"Argos helper returned unexpected segment id {segment.Id}.");
                        result[segment.Id - 1] = segment.Text;
                        break;
                    case LocalProgressEvent progress when progress.JobId == jobId:
                        StatusProgress?.Report($"Argos: tłumaczenie lokalne {progress.Completed} / {progress.Total}…");
                        break;
                    case CompleteEvent complete when complete.JobId == jobId:
                        if (result.Any(text => text is null))
                            throw new InvalidDataException("Argos helper completed before returning all translated segments.");
                        return result.Select(text => text!).ToArray();
                    case LocalErrorEvent error when error.JobId is null || error.JobId == jobId:
                        throw new InvalidOperationException($"Argos helper: {error.Code}: {error.Message}");
                }
            }
        }
        catch
        {
            if (_process is { HasExited: true })
                await StopProcessCoreAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureReadyCoreAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (_loaded && IsRunning)
            return;

        var modelPath = await assetManager.EnsureModelAsync(status, cancellationToken);
        if (!File.Exists(options.HelperPath))
            throw new FileNotFoundException(
                "Nie znaleziono lokalnego helpera Argos. Zainstaluj pełny build SubFlow.",
                options.HelperPath);

        if (_process is { HasExited: false })
            await StopProcessCoreAsync();

        Directory.CreateDirectory(options.PackagesDirectory);
        status?.Report("Argos: uruchamiam lokalny translator…");

        var startInfo = new ProcessStartInfo
        {
            FileName = options.HelperPath,
            WorkingDirectory = options.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = System.Text.Encoding.UTF8,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        startInfo.Environment["ARGOS_PACKAGES_DIR"] = options.PackagesDirectory;
        startInfo.Environment["PYTHONUTF8"] = "1";

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Nie udało się uruchomić lokalnego translatora Argos.");

        _process = process;
        _loaded = false;
        _lifetimeCancellation = new CancellationTokenSource();
        _stderrDrain = DrainErrorAsync(process.StandardError, _lifetimeCancellation.Token);

        try
        {
            await SendCommandAsync(process, new LoadCommand(modelPath), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            while (true)
            {
                var @event = await ReadEventAsync(process, timeout.Token);
                switch (@event)
                {
                    case ReadyEvent:
                        _loaded = true;
                        status?.Report("Argos: lokalny translator EN→PL gotowy.");
                        return;
                    case LocalErrorEvent error:
                        throw new InvalidOperationException($"Argos helper: {error.Code}: {error.Message}");
                }
            }
        }
        catch
        {
            await StopProcessCoreAsync();
            throw;
        }
    }

    private static async Task SendCommandAsync(
        Process process,
        LocalTranslatorCommand command,
        CancellationToken cancellationToken)
    {
        if (process.HasExited)
            throw new InvalidOperationException($"Argos helper zakończył pracę (kod {process.ExitCode}).");

        var line = LocalTranslatorProtocol.SerializeCommand(command);
        await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await process.StandardInput.FlushAsync(cancellationToken);
    }

    private static async Task<LocalTranslatorEvent> ReadEventAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            var suffix = process.HasExited ? $" (kod {process.ExitCode})" : string.Empty;
            throw new InvalidOperationException("Argos helper zamknął kanał odpowiedzi" + suffix + ".");
        }
        return LocalTranslatorProtocol.ParseEvent(line);
    }

    private static async Task DrainErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await reader.ReadLineAsync(cancellationToken) is not null)
            {
                // Keep stderr drained so the child cannot block. Subtitle/model data is not logged.
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private async Task StopProcessCoreAsync()
    {
        var process = _process;
        _process = null;
        _loaded = false;

        if (process is not null)
        {
            if (!process.HasExited)
            {
                try
                {
                    await SendCommandAsync(process, new ShutdownCommand(), CancellationToken.None);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
                }
            }
            process.Dispose();
        }

        _lifetimeCancellation?.Cancel();
        if (_stderrDrain is not null)
        {
            try { await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        _stderrDrain = null;
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try { await StopProcessCoreAsync(); }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
