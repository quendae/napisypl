using System.Diagnostics;

namespace NapisyPL.Core.ContextResolution;

public sealed class LocalContextRuntimeManager(
    HttpClient httpClient,
    LocalContextAssetManager assetManager,
    LocalContextRuntimeOptions options) : IAsyncDisposable
{
    private Process? _process;
    private Task? _stdoutDrain;
    private Task? _stderrDrain;

    public bool IsRunning => _process is { HasExited: false };

    public async Task EnsureRunningAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (await IsHealthyAsync(cancellationToken))
            return;

        await assetManager.EnsureAvailableAsync(status, cancellationToken);

        if (_process is { HasExited: false })
            KillProcess();

        status?.Report("Enhanced: uruchamiam lokalny resolver kontekstu…");

        var startInfo = new ProcessStartInfo
        {
            FileName = options.ServerExecutablePath,
            WorkingDirectory = options.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in options.BuildServerArguments(options.ModelPath))
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Nie udało się uruchomić lokalnego resolvera kontekstu.");

        _process = process;
        _stdoutDrain = DrainAsync(process.StandardOutput, cancellationToken);
        _stderrDrain = DrainAsync(process.StandardError, cancellationToken);

        try
        {
            await WaitForHealthyAsync(process, status, cancellationToken);
            status?.Report("Enhanced: lokalny resolver kontekstu jest gotowy.");
        }
        catch
        {
            KillProcess();
            throw;
        }
    }

    private async Task WaitForHealthyAsync(
        Process process,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));

        var attempt = 0;
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            if (process.HasExited)
                throw new InvalidOperationException($"Lokalny resolver kontekstu zakończył pracę podczas uruchamiania (kod {process.ExitCode}).");

            if (await IsHealthyAsync(timeout.Token))
                return;

            attempt++;
            if (attempt % 10 == 0)
                status?.Report("Enhanced: ładuję model kontekstu…");

            await Task.Delay(500, timeout.Token);
        }
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{options.Port}/health");
            using var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private static async Task DrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && await reader.ReadLineAsync(cancellationToken) is not null)
            {
                // Intentionally drained to prevent a blocked child process. Detailed model output is not logged.
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

    private void KillProcess()
    {
        var process = _process;
        _process = null;
        if (process is null)
            return;

        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        KillProcess();

        var drains = new[] { _stdoutDrain, _stderrDrain }.Where(task => task is not null).Cast<Task>().ToArray();
        if (drains.Length > 0)
        {
            try { await Task.WhenAll(drains).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
        }

        _stdoutDrain = null;
        _stderrDrain = null;
    }
}
