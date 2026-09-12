using System.Security.Cryptography;
using System.Text;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class FirefoxBergamotAssetManager(
    OfflineMtAssetManager assetManager,
    Func<string, CancellationToken, Task<byte[]>> downloadAsync,
    Func<byte[], byte[]> decompressZstd)
{
    private const string BackendId = "bergamot-firefox-en-pl";
    private const string EngineVersion = "0.4.5";

    private readonly OfflineMtAssetManager _assetManager = assetManager ?? throw new ArgumentNullException(nameof(assetManager));
    private readonly Func<string, CancellationToken, Task<byte[]>> _downloadAsync = downloadAsync ?? throw new ArgumentNullException(nameof(downloadAsync));
    private readonly Func<byte[], byte[]> _decompressZstd = decompressZstd ?? throw new ArgumentNullException(nameof(decompressZstd));

    public Task InstallResolvedModelAsync(
        BergamotModelDescriptor descriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return _assetManager.InstallAsync(
            (temporaryDirectory, ct) => InstallIntoTemporaryDirectoryAsync(temporaryDirectory, descriptor, ct),
            cancellationToken);
    }

    private async Task<OfflineMtManifest> InstallIntoTemporaryDirectoryAsync(
        string temporaryDirectory,
        BergamotModelDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        var assets = DistinctAssets(descriptor);
        var manifestFiles = new List<OfflineMtManifestFile>(assets.Count + 1);

        foreach (var asset in assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var downloaded = await _downloadAsync(asset.Url, cancellationToken);
            ValidateBytes(downloaded, asset.DownloadSizeBytes, asset.DownloadSha256, "downloaded");

            var installed = asset.IsZstdCompressed ? _decompressZstd(downloaded) : downloaded;
            ValidateBytes(installed, asset.SizeBytes, asset.Sha256, "decompressed");

            var destination = ResolveChildPath(temporaryDirectory, asset.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllBytesAsync(destination, installed, cancellationToken);
            manifestFiles.Add(new OfflineMtManifestFile(asset.FileName, asset.Sha256, asset.SizeBytes));
        }

        const string configFileName = "config.yml";
        var configPath = Path.Combine(temporaryDirectory, configFileName);
        var configText = BuildConfig(descriptor);
        await File.WriteAllTextAsync(configPath, configText, new UTF8Encoding(false), cancellationToken);
        var configBytes = await File.ReadAllBytesAsync(configPath, cancellationToken);
        manifestFiles.Add(new OfflineMtManifestFile(configFileName, Sha256(configBytes), configBytes.LongLength));

        var installedSize = manifestFiles.Sum(file => file.SizeBytes);
        return new OfflineMtManifest(
            BackendId: BackendId,
            EngineVersion: EngineVersion,
            ModelId: $"firefox-translations-{descriptor.SourceLanguage}-{descriptor.TargetLanguage}",
            ModelVersion: descriptor.ModelVersion,
            ModelSource: descriptor.Model.Url,
            LicenseId: "NOASSERTION",
            BenchmarkOnly: true,
            InstalledSizeBytes: installedSize,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            Files: manifestFiles);
    }

    private static List<BergamotRemoteAsset> DistinctAssets(BergamotModelDescriptor descriptor)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<BergamotRemoteAsset>();
        foreach (var asset in new[] { descriptor.Model, descriptor.SourceVocab, descriptor.TargetVocab, descriptor.Shortlist })
        {
            if (seen.Add(asset.FileName))
                result.Add(asset);
        }

        return result;
    }

    private static void ValidateBytes(byte[] bytes, long expectedSize, string expectedSha256, string stage)
    {
        if (expectedSize >= 0 && bytes.LongLength != expectedSize)
            throw new InvalidDataException($"Firefox Bergamot {stage} asset size mismatch.");

        if (!string.IsNullOrWhiteSpace(expectedSha256) &&
            !string.Equals(Sha256(bytes), expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Firefox Bergamot {stage} asset SHA-256 mismatch.");
    }

    private static string ResolveChildPath(string rootDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new InvalidDataException("Firefox Bergamot asset path is invalid.");

        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootDirectory, relativePath));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Firefox Bergamot asset path escapes the installation directory.");

        return candidate;
    }

    private static string BuildConfig(BergamotModelDescriptor descriptor)
    {
        var builder = new StringBuilder();
        builder.AppendLine("models:");
        builder.Append("  - ").AppendLine(YamlScalar(descriptor.Model.FileName));
        builder.AppendLine("vocabs:");
        builder.Append("  - ").AppendLine(YamlScalar(descriptor.SourceVocab.FileName));
        builder.Append("  - ").AppendLine(YamlScalar(descriptor.TargetVocab.FileName));
        builder.AppendLine("shortlist:");
        builder.Append("  - ").AppendLine(YamlScalar(descriptor.Shortlist.FileName));
        builder.AppendLine("  - false");
        builder.AppendLine("beam-size: 1");
        builder.AppendLine("normalize: 1.0");
        builder.AppendLine("word-penalty: 0");
        builder.AppendLine("max-length-break: 128");
        builder.AppendLine("mini-batch-words: 1024");
        builder.AppendLine("workspace: 128");
        builder.AppendLine("max-length-factor: 2.0");
        builder.AppendLine("skip-cost: true");
        builder.AppendLine("cpu-threads: 0");
        builder.AppendLine("quiet: true");
        builder.AppendLine("quiet-translation: true");
        builder.AppendLine("gemm-precision: int8shiftAlphaAll");
        builder.AppendLine("alignment: soft");
        return builder.ToString();
    }

    private static string YamlScalar(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
