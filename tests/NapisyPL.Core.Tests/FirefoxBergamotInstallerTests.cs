using System.Security.Cryptography;
using System.Text;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class FirefoxBergamotInstallerTests
{
    [Fact]
    public async Task InstallAsync_VerifiesTransportAndInstalledAssets_ThenPromotesAtomically()
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-firefox-installer-" + Guid.NewGuid().ToString("N"));
        var backendDirectory = Path.Combine(root, "bergamot-firefox");
        var assetManager = new OfflineMtAssetManager(backendDirectory);
        var fixture = CreateFixture();
        var installer = new FirefoxBergamotAssetManager(
            assetManager,
            (url, _) => Task.FromResult(fixture.Downloads[url]),
            compressed => fixture.Decompressed[Convert.ToBase64String(compressed)]);

        try
        {
            await installer.InstallResolvedModelAsync(fixture.Descriptor);

            Assert.True(await assetManager.IsInstalledAsync());
            Assert.Equal(fixture.ModelRaw, await File.ReadAllBytesAsync(Path.Combine(backendDirectory, "model.enpl.bin")));
            Assert.Equal(fixture.VocabRaw, await File.ReadAllBytesAsync(Path.Combine(backendDirectory, "vocab.enpl.spm")));
            Assert.Equal(fixture.LexRaw, await File.ReadAllBytesAsync(Path.Combine(backendDirectory, "lex.enpl.bin")));

            var config = await File.ReadAllTextAsync(Path.Combine(backendDirectory, "config.yml"));
            Assert.Contains("model.enpl.bin", config, StringComparison.Ordinal);
            Assert.Contains("vocab.enpl.spm", config, StringComparison.Ordinal);
            Assert.Contains("lex.enpl.bin", config, StringComparison.Ordinal);
            Assert.DoesNotContain("transport-model.zst", config, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallAsync_BadTransportHash_DoesNotPromotePartialInstallation()
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-firefox-installer-" + Guid.NewGuid().ToString("N"));
        var backendDirectory = Path.Combine(root, "bergamot-firefox");
        var assetManager = new OfflineMtAssetManager(backendDirectory);
        var fixture = CreateFixture();
        fixture.Descriptor.Model.DownloadSha256 = new string('0', 64);
        var installer = new FirefoxBergamotAssetManager(
            assetManager,
            (url, _) => Task.FromResult(fixture.Downloads[url]),
            compressed => fixture.Decompressed[Convert.ToBase64String(compressed)]);

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallResolvedModelAsync(fixture.Descriptor));

            Assert.False(await assetManager.IsInstalledAsync());
            Assert.False(Directory.Exists(backendDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static Fixture CreateFixture()
    {
        var modelRaw = Encoding.UTF8.GetBytes("raw-model");
        var vocabRaw = Encoding.UTF8.GetBytes("raw-vocab");
        var lexRaw = Encoding.UTF8.GetBytes("raw-lex");
        var modelCompressed = Encoding.UTF8.GetBytes("zst-model");
        var vocabCompressed = Encoding.UTF8.GetBytes("zst-vocab");
        var lexCompressed = Encoding.UTF8.GetBytes("zst-lex");

        const string modelUrl = "https://example.invalid/model.zst";
        const string vocabUrl = "https://example.invalid/vocab.zst";
        const string lexUrl = "https://example.invalid/lex.zst";

        var descriptor = new BergamotModelDescriptor
        {
            SourceLanguage = "en",
            TargetLanguage = "pl",
            ModelVersion = "3.0",
            Model = Asset("model", "model.enpl.bin", "transport-model.zst", modelUrl, modelRaw, modelCompressed),
            SourceVocab = Asset("vocab", "vocab.enpl.spm", "transport-vocab.zst", vocabUrl, vocabRaw, vocabCompressed),
            TargetVocab = Asset("vocab", "vocab.enpl.spm", "transport-vocab.zst", vocabUrl, vocabRaw, vocabCompressed),
            Shortlist = Asset("lex", "lex.enpl.bin", "transport-lex.zst", lexUrl, lexRaw, lexCompressed),
        };

        return new Fixture(
            descriptor,
            modelRaw,
            vocabRaw,
            lexRaw,
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [modelUrl] = modelCompressed,
                [vocabUrl] = vocabCompressed,
                [lexUrl] = lexCompressed,
            },
            new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                [Convert.ToBase64String(modelCompressed)] = modelRaw,
                [Convert.ToBase64String(vocabCompressed)] = vocabRaw,
                [Convert.ToBase64String(lexCompressed)] = lexRaw,
            });
    }

    private static BergamotRemoteAsset Asset(
        string fileType,
        string fileName,
        string downloadFileName,
        string url,
        byte[] raw,
        byte[] compressed) => new()
    {
        FileType = fileType,
        FileName = fileName,
        Url = url,
        Sha256 = Sha256(raw),
        SizeBytes = raw.LongLength,
        DownloadSha256 = Sha256(compressed),
        DownloadSizeBytes = compressed.LongLength,
        DownloadFileName = downloadFileName,
        IsZstdCompressed = true,
    };

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record Fixture(
        BergamotModelDescriptor Descriptor,
        byte[] ModelRaw,
        byte[] VocabRaw,
        byte[] LexRaw,
        IReadOnlyDictionary<string, byte[]> Downloads,
        IReadOnlyDictionary<string, byte[]> Decompressed);
}
