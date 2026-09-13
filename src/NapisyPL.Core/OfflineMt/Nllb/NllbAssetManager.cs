using System.Net.Http;
using System.Security.Cryptography;

namespace NapisyPL.Core.OfflineMt.Nllb;

public sealed class NllbAssetManager
{
    public const string EngineVersion = "transformers-4.57.6";

    private readonly OfflineMtAssetManager _assetManager;
    private readonly Func<string, string, CancellationToken, Task> _downloadToFileAsync;

    public NllbAssetManager(
        OfflineMtAssetManager assetManager,
        Func<string, string, CancellationToken, Task> downloadToFileAsync)
    {
        _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
        _downloadToFileAsync = downloadToFileAsync ?? throw new ArgumentNullException(nameof(downloadToFileAsync));
    }

    public NllbAssetManager(HttpClient httpClient, OfflineMtAssetManager assetManager)
        : this(assetManager, CreateHttpDownloader(httpClient))
    {
    }

    public Task InstallPinnedModelAsync(CancellationToken cancellationToken = default)
    {
        var descriptor = NllbModelDescriptor.Pinned;
        return _assetManager.InstallAsync(
            (temporaryDirectory, ct) => InstallIntoTemporaryDirectoryAsync(
                temporaryDirectory,
                descriptor,
                ct),
            cancellationToken);
    }

    private async Task<OfflineMtManifest> InstallIntoTemporaryDirectoryAsync(
        string temporaryDirectory,
        NllbModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var manifestFiles = new List<OfflineMtManifestFile>(descriptor.Files.Count);

        foreach (var file in descriptor.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = ResolveDestinationPath(temporaryDirectory, file.RelativePath);
            var destinationDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(destinationDirectory))
                Directory.CreateDirectory(destinationDirectory);

            await _downloadToFileAsync(file.DownloadUrl, destinationPath, cancellationToken);
            if (!File.Exists(destinationPath))
                throw new InvalidDataException($"NLLB download did not produce '{file.RelativePath}'.");

            manifestFiles.Add(await CreateManifestFileAsync(
                temporaryDirectory,
                destinationPath,
                cancellationToken));
        }

        return new OfflineMtManifest(
            BackendId: descriptor.BackendId,
            EngineVersion: EngineVersion,
            ModelId: descriptor.ModelId,
            ModelVersion: descriptor.Revision,
            ModelSource: descriptor.RepositoryTreeUrl,
            LicenseId: descriptor.LicenseId,
            BenchmarkOnly: descriptor.BenchmarkOnly,
            InstalledSizeBytes: manifestFiles.Sum(file => file.SizeBytes),
            InstalledAtUtc: DateTimeOffset.UtcNow,
            Files: manifestFiles);
    }

    private static Func<string, string, CancellationToken, Task> CreateHttpDownloader(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        return async (url, destinationPath, cancellationToken) =>
        {
            using var response = await httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true);
            await source.CopyToAsync(destination, 1024 * 1024, cancellationToken);
        };
    }

    private static string ResolveDestinationPath(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("NLLB model file path must be relative.");

        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var destination = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NLLB model file path escapes the installation directory.");

        return destination;
    }

    private static async Task<OfflineMtManifestFile> CreateManifestFileAsync(
        string rootDirectory,
        string fullPath,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(fullPath);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken))
            .ToLowerInvariant();
        var info = new FileInfo(fullPath);
        var relativePath = Path.GetRelativePath(rootDirectory, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return new OfflineMtManifestFile(relativePath, hash, info.Length);
    }
}
