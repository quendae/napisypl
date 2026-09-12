using System.Diagnostics;
using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class BergamotProcessRuntimeChannel : IBergamotRuntimeChannel
{
    private readonly Process _process;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly Task _stderrDrain;
    private bool _disposed;

    public BergamotProcessRuntimeChannel(BergamotRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.HelperPath))
            throw new ArgumentException("Bergamot helper path cannot be empty.", nameof(options));
        if (!File.Exists(options.HelperPath))
            throw new FileNotFoundException(
                "Bergamot helper was not found. Install a full SubFlow build containing SubFlow.BergamotHelper.exe.",
                options.HelperPath);

        var fullHelperPath = Path.GetFullPath(options.HelperPath);
        var startInfo = new ProcessStartInfo
        {
            FileName = fullHelperPath,
            WorkingDirectory = Path.GetDirectoryName(fullHelperPath) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = LocalTranslatorEncoding.Utf8NoBom,
            StandardOutputEncoding = LocalTranslatorEncoding.Utf8NoBom,
            StandardErrorEncoding = LocalTranslatorEncoding.Utf8NoBom
        };

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_process.Start())
        {
            _process.Dispose();
            throw new InvalidOperationException("Could not start the Bergamot translation helper.");
        }

        _stderrDrain = DrainErrorAsync(_process.StandardError, _lifetimeCancellation.Token);
    }

    public bool IsRunning => !_disposed && !_process.HasExited;
    public int? ProcessId => IsRunning ? _process.Id : null;

    public async Task SendAsync(
        LocalTranslatorCommand command,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_process.HasExited)
            throw new InvalidOperationException($"Bergamot helper exited with code {_process.ExitCode}.");

        var line = LocalTranslatorProtocol.SerializeCommand(command);
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
    }

    public async Task<LocalTranslatorEvent> ReadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null)
        {
            var suffix = _process.HasExited ? $" (code {_process.ExitCode})" : string.Empty;
            throw new InvalidOperationException("Bergamot helper closed its response channel" + suffix + ".");
        }

        return LocalTranslatorProtocol.ParseEvent(line);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _process.HasExited)
            return;

        try
        {
            await SendAsync(new ShutdownCommand(), cancellationToken);
            await _process.WaitForExitAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }
        catch
        {
            try
            {
                if (!_process.HasExited)
                    _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Process cleanup is best-effort after a failed graceful shutdown.
            }
        }
    }

    private static async Task DrainErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   await reader.ReadLineAsync(cancellationToken) is not null)
            {
                // Keep stderr drained so the helper cannot block. Never log subtitle/model content here.
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await StopAsync(CancellationToken.None);
        }
        finally
        {
            _disposed = true;
            _lifetimeCancellation.Cancel();
            try { await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _process.Dispose();
            _lifetimeCancellation.Dispose();
        }
    }
}
