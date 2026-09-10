using System.IO.Compression;

namespace NapisyPL.Core.ContextResolution;

public sealed class LocalContextAssetManager(
    HttpClient httpClient,
    LocalContextRuntimeOptions options)
{
    public async Task EnsureAvailableAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Automatyczna instalacja lokalnego resolvera jest obecnie dostępna na Windows.");

        Directory.CreateDirectory(options.BaseDirectory);
        Directory.CreateDirectory(options.RuntimeDirectory);
        Directory.CreateDirectory(options.ModelDirectory);

        if (!File.Exists(options.ServerExecutablePath))
            await DownloadRuntimeAsync(status, cancellationToken);

        if (!File.Exists(options.ModelPath))
            await DownloadModelAsync(status, cancellationToken);
    }

    private async Task DownloadRuntimeAsync(IProgress<string>? status, CancellationToken cancellationToken)
    {
        status?.Report("Enhanced: pobieram lokalny silnik kontekstu (~20 MB)…");
        var tempRoot = Path.Combine(Path.GetTempPath(), "SubFlow-llama-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(tempRoot, "runtime.zip");
        var extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(extractPath);

        try
        {
            await DownloadToFileAsync(options.RuntimeZipUrl, zipPath, cancellationToken);
            ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);

            var serverPath = Directory.EnumerateFiles(extractPath, "llama-server.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (serverPath is null)
                throw new InvalidDataException("Pobrany pakiet llama.cpp nie zawiera llama-server.exe.");

            var sourceDirectory = Path.GetDirectoryName(serverPath)!;
            CopyDirectory(sourceDirectory, options.RuntimeDirectory);

            if (!File.Exists(options.ServerExecutablePath))
                throw new InvalidDataException("Nie udało się zainstalować llama-server.exe.");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private async Task DownloadModelAsync(IProgress<string>? status, CancellationToken cancellationToken)
    {
        status?.Report("Enhanced: pobieram model kontekstu Qwen3-1.7B (~1.3 GB)…");
        var partialPath = options.ModelPath + ".partial";
        try
        {
            await DownloadToFileAsync(options.ModelUrl, partialPath, cancellationToken);
            File.Move(partialPath, options.ModelPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partialPath))
            {
                try { File.Delete(partialPath); } catch { }
            }
        }
    }

    private async Task DownloadToFileAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
    }
}
