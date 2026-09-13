using System.Collections.Concurrent;
using System.Diagnostics;
using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.OfflineMt.Nllb;

public sealed class NllbProcessRuntimeChannel : INllbRuntimeChannel
{
    private readonly Process _process;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _stderrDrain;
    private readonly ConcurrentQueue<string> _stderrTail = new();
    private bool _disposed;

    public NllbProcessRuntimeChannel(NllbRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.PythonPath))
            throw new ArgumentException("NLLB runtime executable path cannot be empty.", nameof(options));

        var packagedHelper = string.IsNullOrWhiteSpace(options.HelperPath);
        if (!packagedHelper && !File.Exists(options.HelperPath))
            throw new FileNotFoundException("NLLB helper was not found.", options.HelperPath);
        if (packagedHelper && !File.Exists(options.PythonPath))
            throw new FileNotFoundException("Packaged NLLB helper was not found.", options.PythonPath);

        var workingTarget = packagedHelper ? options.PythonPath : options.HelperPath;
        var startInfo = new ProcessStartInfo
        {
            FileName = options.PythonPath,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(workingTarget)) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = LocalTranslatorEncoding.Utf8NoBom,
            StandardOutputEncoding = LocalTranslatorEncoding.Utf8NoBom,
            StandardErrorEncoding = LocalTranslatorEncoding.Utf8NoBom
        };
        if (!packagedHelper)
            startInfo.ArgumentList.Add(options.HelperPath);
        startInfo.Environment["PYTHONUTF8"] = "1";

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_process.Start())
            throw new InvalidOperationException("Failed to start NLLB helper.");

        _stderrDrain = DrainErrorAsync(_process.StandardError, _lifetime.Token);
    }

    public bool IsRunning => !_disposed && !_process.HasExited;
    public int? ProcessId => IsRunning ? _process.Id : null;

    public async Task SendAsync(
        LocalTranslatorCommand command,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process.HasExited)
            throw ClosedChannelException();

        var line = LocalTranslatorProtocol.SerializeCommand(command);
        await _process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken);
        await _process.StandardInput.FlushAsync(cancellationToken);
    }

    public async Task<LocalTranslatorEvent> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
        if (line is null)
            throw ClosedChannelException();
        return LocalTranslatorProtocol.ParseEvent(line);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            return;

        if (!_process.HasExited)
        {
            try
            {
                await SendAsync(new ShutdownCommand(), cancellationToken);
                await _process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
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
                }
            }
        }
    }

    private async Task DrainErrorAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;
                _stderrTail.Enqueue(line);
                while (_stderrTail.Count > 40 && _stderrTail.TryDequeue(out _))
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
    }

    private InvalidOperationException ClosedChannelException()
    {
        var exit = _process.HasExited ? $" (code {_process.ExitCode})" : string.Empty;
        var diagnostics = string.Join(Environment.NewLine, _stderrTail.ToArray());
        var suffix = string.IsNullOrWhiteSpace(diagnostics)
            ? string.Empty
            : Environment.NewLine + "NLLB native diagnostics:" + Environment.NewLine + diagnostics;
        return new InvalidOperationException("NLLB helper closed its response channel" + exit + "." + suffix);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        try
        {
            await StopAsync();
        }
        finally
        {
            _disposed = true;
            _lifetime.Cancel();
            try { await _stderrDrain.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            _lifetime.Dispose();
            _process.Dispose();
        }
    }
}
