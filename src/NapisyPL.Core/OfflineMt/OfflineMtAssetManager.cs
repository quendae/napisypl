using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NapisyPL.Core.OfflineMt;

public sealed class OfflineMtAssetManager(string backendDirectory)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public string BackendDirectory { get; } = backendDirectory ?? throw new ArgumentNullException(nameof(backendDirectory));
    public string ManifestPath => Path.Combine(BackendDirectory, "manifest.json");

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(ManifestPath))
            return false;

        try
        {
            var json = await File.ReadAllTextAsync(ManifestPath, Encoding.UTF8, cancellationToken);
            var manifest = JsonSerializer.Deserialize<OfflineMtManifest>(json, JsonOptions);
            return manifest is not null && await ValidateManifestAsync(BackendDirectory, manifest, cancellationToken);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async Task WriteManifestAsync(
        OfflineMtManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Directory.CreateDirectory(BackendDirectory);
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(ManifestPath, json, new UTF8Encoding(false), cancellationToken);
    }

    public async Task InstallAsync(
        Func<string, CancellationToken, Task<OfflineMtManifest>> installer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installer);

        var parent = Directory.GetParent(BackendDirectory)?.FullName
            ?? throw new InvalidOperationException("Offline MT backend directory must have a parent directory.");
        Directory.CreateDirectory(parent);

        var backendName = Path.GetFileName(BackendDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var temporaryDirectory = Path.Combine(parent, $".tmp-{backendName}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            var manifest = await installer(temporaryDirectory, cancellationToken);
            if (!await ValidateManifestAsync(temporaryDirectory, manifest, cancellationToken))
                throw new InvalidDataException("Offline MT installation failed manifest/hash validation.");

            var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);
            await File.WriteAllTextAsync(
                Path.Combine(temporaryDirectory, "manifest.json"),
                manifestJson,
                new UTF8Encoding(false),
                cancellationToken);

            PromoteDirectory(temporaryDirectory, BackendDirectory);
        }
        finally
        {
            TryDeleteDirectory(temporaryDirectory);
        }
    }

    public Task CleanupTemporaryDirectoriesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var parent = Directory.GetParent(BackendDirectory)?.FullName;
        if (parent is null || !Directory.Exists(parent))
            return Task.CompletedTask;

        var backendName = Path.GetFileName(BackendDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        foreach (var directory in Directory.GetDirectories(parent, $".tmp-{backendName}-*", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteDirectory(directory);
        }

        return Task.CompletedTask;
    }

    private static async Task<bool> ValidateManifestAsync(
        string rootDirectory,
        OfflineMtManifest manifest,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(manifest.BackendId) ||
            string.IsNullOrWhiteSpace(manifest.ModelId) ||
            string.IsNullOrWhiteSpace(manifest.ModelVersion) ||
            manifest.Files is null ||
            manifest.Files.Count == 0)
            return false;

        long totalSize = 0;
        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryResolveChildPath(rootDirectory, file.RelativePath, out var fullPath) ||
                !File.Exists(fullPath))
                return false;

            var info = new FileInfo(fullPath);
            if (file.SizeBytes >= 0 && info.Length != file.SizeBytes)
                return false;

            await using var stream = File.OpenRead(fullPath);
            var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                return false;

            totalSize += info.Length;
        }

        return manifest.InstalledSizeBytes < 0 || totalSize <= manifest.InstalledSizeBytes;
    }

    private static bool TryResolveChildPath(string rootDirectory, string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return false;

        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = candidate;
        return true;
    }

    private static void PromoteDirectory(string temporaryDirectory, string finalDirectory)
    {
        if (!Directory.Exists(finalDirectory))
        {
            Directory.Move(temporaryDirectory, finalDirectory);
            return;
        }

        var backupDirectory = finalDirectory + ".old-" + Guid.NewGuid().ToString("N");
        Directory.Move(finalDirectory, backupDirectory);
        try
        {
            Directory.Move(temporaryDirectory, finalDirectory);
            TryDeleteDirectory(backupDirectory);
        }
        catch
        {
            if (!Directory.Exists(finalDirectory) && Directory.Exists(backupDirectory))
                Directory.Move(backupDirectory, finalDirectory);
            throw;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A stale temp/backup directory is not considered a valid installation.
        }
    }
}
