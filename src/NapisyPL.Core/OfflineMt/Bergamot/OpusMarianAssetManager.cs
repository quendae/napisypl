using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class OpusMarianAssetManager(
    OfflineMtAssetManager assetManager,
    Func<string, CancellationToken, Task<byte[]>> downloadAsync)
{
    private const string EngineVersion = "bergamot-translator-0.4.5";

    private readonly OfflineMtAssetManager _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    private readonly Func<string, CancellationToken, Task<byte[]>> _downloadAsync = downloadAsync ?? throw new ArgumentNullException(nameof(downloadAsync));

    public async Task InstallPinnedModelAsync(CancellationToken cancellationToken = default)
    {
        var descriptor = OpusMarianModelDescriptor.Pinned;
        var archiveBytes = await _downloadAsync(descriptor.ArchiveUrl, cancellationToken);
        if (archiveBytes.Length == 0)
            throw new InvalidDataException("OPUS Marian model archive is empty.");

        await _assetManager.InstallAsync(
            (temporaryDirectory, ct) => InstallIntoTemporaryDirectoryAsync(
                temporaryDirectory,
                descriptor,
                archiveBytes,
                ct),
            cancellationToken);
    }

    private static async Task<OfflineMtManifest> InstallIntoTemporaryDirectoryAsync(
        string temporaryDirectory,
        OpusMarianModelDescriptor descriptor,
        byte[] archiveBytes,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream(archiveBytes, writable: false);
        using var archive = new ZipArchive(buffer, ZipArchiveMode.Read, leaveOpen: false);

        foreach (var entry in archive.Entries.Where(entry => !string.IsNullOrEmpty(entry.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = ResolveArchiveEntryPath(temporaryDirectory, entry.FullName);
        }

        var modelEntry = FindRequiredEntry(archive, "model.npz");
        var sourceVocabEntry = FindRequiredEntry(archive, "source.spm");
        var targetVocabEntry = FindRequiredEntry(archive, "target.spm");

        var manifestFiles = new List<OfflineMtManifestFile>(4)
        {
            await CopyCanonicalEntryAsync(modelEntry, temporaryDirectory, "model.npz", cancellationToken),
            await CopyCanonicalEntryAsync(sourceVocabEntry, temporaryDirectory, "source.spm", cancellationToken),
            await CopyCanonicalEntryAsync(targetVocabEntry, temporaryDirectory, "target.spm", cancellationToken)
        };

        const string configFileName = "config.yml";
        var configBytes = new UTF8Encoding(false).GetBytes(BuildConfig());
        await File.WriteAllBytesAsync(
            Path.Combine(temporaryDirectory, configFileName),
            configBytes,
            cancellationToken);
        manifestFiles.Add(ManifestFile(configFileName, configBytes));

        return new OfflineMtManifest(
            BackendId: descriptor.BackendId,
            EngineVersion: EngineVersion,
            ModelId: descriptor.BackendId,
            ModelVersion: descriptor.Release,
            ModelSource: descriptor.ArchiveUrl,
            LicenseId: "NOASSERTION",
            BenchmarkOnly: true,
            InstalledSizeBytes: manifestFiles.Sum(file => file.SizeBytes),
            InstalledAtUtc: DateTimeOffset.UtcNow,
            Files: manifestFiles);
    }

    private static ZipArchiveEntry FindRequiredEntry(ZipArchive archive, string fileName)
    {
        var matches = archive.Entries
            .Where(entry => string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidDataException($"OPUS Marian archive is missing required file '{fileName}'."),
            _ => throw new InvalidDataException($"OPUS Marian archive contains multiple '{fileName}' files.")
        };
    }

    private static async Task<OfflineMtManifestFile> CopyCanonicalEntryAsync(
        ZipArchiveEntry entry,
        string temporaryDirectory,
        string destinationFileName,
        CancellationToken cancellationToken)
    {
        var destinationPath = Path.Combine(temporaryDirectory, destinationFileName);
        await using (var source = entry.Open())
        await using (var destination = File.Create(destinationPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        var bytes = await File.ReadAllBytesAsync(destinationPath, cancellationToken);
        return ManifestFile(destinationFileName, bytes);
    }

    private static OfflineMtManifestFile ManifestFile(string relativePath, byte[] bytes) =>
        new(
            relativePath,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            bytes.LongLength);

    private static string BuildConfig() =>
        "models:\n" +
        "  - 'model.npz'\n" +
        "vocabs:\n" +
        "  - 'source.spm'\n" +
        "  - 'target.spm'\n" +
        "beam-size: 1\n" +
        "normalize: 1.0\n" +
        "word-penalty: 0\n" +
        "max-length-break: 128\n" +
        "mini-batch-words: 1024\n" +
        "workspace: 128\n" +
        "max-length-factor: 2.0\n" +
        "skip-cost: true\n" +
        "cpu-threads: 0\n" +
        "quiet: true\n" +
        "quiet-translation: true\n";

    public static string ResolveArchiveEntryPath(string rootDirectory, string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        if (Path.IsPathRooted(entryName) || LooksLikeWindowsAbsolutePath(entryName))
        {
            throw new InvalidDataException("OPUS archive contains an absolute path.");
        }

        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedEntry = entryName
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(rootDirectory, normalizedEntry));

        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OPUS archive entry escapes the installation directory.");
        }

        return resolved;
    }

    private static bool LooksLikeWindowsAbsolutePath(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
