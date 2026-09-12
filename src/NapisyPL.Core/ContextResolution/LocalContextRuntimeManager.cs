using System.Diagnostics;
using NapisyPL.Core.Diagnostics;

namespace NapisyPL.Core.ContextResolution;

public sealed class LocalContextRuntimeManager(
    HttpClient httpClient,
    LocalContextAssetManager assetManager,
    LocalContextRuntimeOptions options,
    IAppLogger? logger = null) : IAsyncDisposable
{
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;
    private readonly AsyncSingleFlightGate _startupGate = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private Process? _process;
    private CancellationTokenSource? _lifetimeCancellation;
    private Task? _stdoutDrain;
    private Task? _stderrDrain;

    public bool IsRunning => _process is { HasExited: false };

    public async Task EnsureRunningAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (IsRunning && await IsHealthyAsync(cancellationToken))
            return;

        await _startupGate.RunAsync(
            token => EnsureRunningCoreAsync(status, token),
            _disposeCancellation.Token,
            cancellationToken);
    }

    private async Task EnsureRunningCoreAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (IsRunning && await IsHealthyAsync(cancellationToken))
            return;

        await assetManager.EnsureAvailableAsync(status, cancellationToken);

        if (_process is { HasExited: false })
            await StopProcessAsync();

        var stopwatch = Stopwatch.StartNew();
        var (effectiveOptions, resolvedDevice) = await ResolveBackendAsync(status, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var effectiveBackend = effectiveOptions.Backend == LocalContextBackend.Cpu ? "cpu" : "vulkan";

        status?.Report(
            $"Enhanced: uruchamiam {effectiveOptions.ModelDisplayName} · {effectiveBackend}" +
            (resolvedDevice is null ? string.Empty : $" · {resolvedDevice}") + "…");

        var startInfo = new ProcessStartInfo
        {
            FileName = effectiveOptions.ServerExecutablePath,
            WorkingDirectory = effectiveOptions.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in effectiveOptions.BuildServerArguments(effectiveOptions.ModelPath, resolvedDevice))
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("Nie udało się uruchomić lokalnego resolvera kontekstu.");

        _process = process;
        _lifetimeCancellation = new CancellationTokenSource();
        _stdoutDrain = DrainAsync(process.StandardOutput, _lifetimeCancellation.Token);
        _stderrDrain = DrainAsync(process.StandardError, _lifetimeCancellation.Token);

        try
        {
            await WaitForHealthyAsync(process, status, cancellationToken);
            stopwatch.Stop();
            _logger.Info(
                "local_context_runtime",
                ("backend", effectiveBackend),
                ("device", resolvedDevice ?? "none"),
                ("model", options.ModelAlias),
                ("version", options.RuntimeVersion),
                ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                ("result", "success"));
            status?.Report(
                $"Enhanced: {effectiveOptions.ModelDisplayName} gotowy · {effectiveBackend}" +
                (resolvedDevice is null ? string.Empty : $" · {resolvedDevice}") + ".");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Error(
                "local_context_runtime",
                ("backend", effectiveBackend),
                ("device", resolvedDevice ?? "none"),
                ("model", options.ModelAlias),
                ("version", options.RuntimeVersion),
                ("elapsedMs", stopwatch.Elapsed.TotalMilliseconds),
                ("category", ex.GetType().Name),
                ("result", "failed"));
            await StopProcessAsync();
            throw;
        }
    }

    private async Task<(LocalContextRuntimeOptions Options, string? Device)> ResolveBackendAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (options.Backend == LocalContextBackend.Cpu)
            return (options, null);

        string? device = null;
        try
        {
            device = await DetectPreferredVulkanDeviceAsync(options, cancellationToken);
        }
        catch when (options.Backend == LocalContextBackend.Auto)
        {
            device = null;
        }

        if (!string.IsNullOrWhiteSpace(device))
            return (options, device);

        if (options.Backend == LocalContextBackend.Vulkan)
            throw new InvalidOperationException(
                "Nie znaleziono urządzenia Vulkan dla llama.cpp. Wybierz backend CPU albo zaktualizuj sterownik Vulkan GPU.");

        status?.Report("Enhanced: Vulkan niedostępny — przechodzę na CPU…");
        var cpuOptions = options.WithBackend(LocalContextBackend.Cpu);
        var cpuAssets = new LocalContextAssetManager(httpClient, cpuOptions);
        await cpuAssets.EnsureAvailableAsync(status, cancellationToken);
        return (cpuOptions, null);
    }

    private static async Task<string?> DetectPreferredVulkanDeviceAsync(
        LocalContextRuntimeOptions runtimeOptions,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = runtimeOptions.ServerExecutablePath,
            WorkingDirectory = runtimeOptions.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--list-devices");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            return null;

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);
        var output = (await stdoutTask) + Environment.NewLine + (await stderrTask);
        return LocalContextDeviceSelector.SelectPreferredVulkanDevice(output);
    }

    private async Task WaitForHealthyAsync(
        Process process,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));

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
                status?.Report($"Enhanced: ładuję {options.ModelDisplayName}…");

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

    private async Task StopProcessAsync()
    {
        var process = _process;
        _process = null;

        _lifetimeCancellation?.Cancel();

        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill(entireProcessTree: true);
            }
            catch
            {
            }

            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
            process.Dispose();
        }

        var drains = new[] { _stdoutDrain, _stderrDrain }.Where(task => task is not null).Cast<Task>().ToArray();
        if (drains.Length > 0)
        {
            try { await Task.WhenAll(drains).WaitAsync(TimeSpan.FromSeconds(2)); }
            catch { }
        }

        _stdoutDrain = null;
        _stderrDrain = null;
        _lifetimeCancellation?.Dispose();
        _lifetimeCancellation = null;
    }

    public async ValueTask DisposeAsync()
    {
        _disposeCancellation.Cancel();
        await StopProcessAsync();
        _disposeCancellation.Dispose();
    }
}
