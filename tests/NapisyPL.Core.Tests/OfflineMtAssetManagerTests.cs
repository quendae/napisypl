using System.Security.Cryptography;
using NapisyPL.Core.OfflineMt;

namespace NapisyPL.Core.Tests;

public sealed class OfflineMtAssetManagerTests
{
    [Fact]
    public async Task IsInstalledAsync_MissingManifest_ReturnsFalse()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var manager = new OfflineMtAssetManager(root);

            Assert.False(await manager.IsInstalledAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IsInstalledAsync_MissingRequiredFile_ReturnsFalse()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            var manager = new OfflineMtAssetManager(root);
            await manager.WriteManifestAsync(new OfflineMtManifest(
                "bergamot-firefox-en-pl",
                "0.4.5",
                "enpl",
                "base",
                "https://example.invalid/model",
                "MPL-2.0",
                false,
                4,
                DateTimeOffset.UnixEpoch,
                [new OfflineMtManifestFile("model/model.bin", Sha256("test"), 4)]));

            Assert.False(await manager.IsInstalledAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IsInstalledAsync_WrongHash_ReturnsFalse()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "model"));
            await File.WriteAllTextAsync(Path.Combine(root, "model", "model.bin"), "nope");
            var manager = new OfflineMtAssetManager(root);
            await manager.WriteManifestAsync(new OfflineMtManifest(
                "bergamot-firefox-en-pl",
                "0.4.5",
                "enpl",
                "base",
                "https://example.invalid/model",
                "MPL-2.0",
                false,
                4,
                DateTimeOffset.UnixEpoch,
                [new OfflineMtManifestFile("model/model.bin", Sha256("test"), 4)]));

            Assert.False(await manager.IsInstalledAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task IsInstalledAsync_ValidManifestAndHashes_ReturnsTrue()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "model"));
            await File.WriteAllTextAsync(Path.Combine(root, "model", "model.bin"), "test");
            var manager = new OfflineMtAssetManager(root);
            await manager.WriteManifestAsync(new OfflineMtManifest(
                "bergamot-firefox-en-pl",
                "0.4.5",
                "enpl",
                "base",
                "https://example.invalid/model",
                "MPL-2.0",
                false,
                4,
                DateTimeOffset.UnixEpoch,
                [new OfflineMtManifestFile("model/model.bin", Sha256("test"), 4)]));

            Assert.True(await manager.IsInstalledAsync());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InstallAsync_PromotesTempDirectoryOnlyAfterValidation()
    {
        var parent = TempDirectory();
        var finalDirectory = Path.Combine(parent, "bergamot");
        try
        {
            Directory.CreateDirectory(parent);
            var manager = new OfflineMtAssetManager(finalDirectory);

            await manager.InstallAsync(async (temporaryDirectory, cancellationToken) =>
            {
                Directory.CreateDirectory(Path.Combine(temporaryDirectory, "model"));
                await File.WriteAllTextAsync(
                    Path.Combine(temporaryDirectory, "model", "model.bin"),
                    "test",
                    cancellationToken);
                return new OfflineMtManifest(
                    "bergamot-firefox-en-pl",
                    "0.4.5",
                    "enpl",
                    "base",
                    "https://example.invalid/model",
                    "MPL-2.0",
                    false,
                    4,
                    DateTimeOffset.UnixEpoch,
                    [new OfflineMtManifestFile("model/model.bin", Sha256("test"), 4)]);
            });

            Assert.True(Directory.Exists(finalDirectory));
            Assert.True(await manager.IsInstalledAsync());
            Assert.Empty(Directory.GetDirectories(parent, ".tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task InstallAsync_InvalidPayload_DoesNotPromoteAndCleansTemporaryDirectory()
    {
        var parent = TempDirectory();
        var finalDirectory = Path.Combine(parent, "opus-marian");
        try
        {
            Directory.CreateDirectory(parent);
            var manager = new OfflineMtAssetManager(finalDirectory);

            await Assert.ThrowsAsync<InvalidDataException>(() => manager.InstallAsync(
                (temporaryDirectory, cancellationToken) => Task.FromResult(new OfflineMtManifest(
                    "opus-marian-eng-pol-2021-02-19",
                    "0.4.5",
                    "eng-pol",
                    "2021-02-19",
                    "https://example.invalid/opus.zip",
                    "CC-BY-4.0",
                    false,
                    4,
                    DateTimeOffset.UnixEpoch,
                    [new OfflineMtManifestFile("model/model.npz", Sha256("test"), 4)]))));

            Assert.False(Directory.Exists(finalDirectory));
            Assert.Empty(Directory.GetDirectories(parent, ".tmp-*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            DeleteDirectory(parent);
        }
    }

    [Fact]
    public async Task CleanupTemporaryDirectoriesAsync_RemovesInterruptedInstall()
    {
        var parent = TempDirectory();
        var finalDirectory = Path.Combine(parent, "nllb-600m");
        var interrupted = Path.Combine(parent, ".tmp-nllb-600m-deadbeef");
        try
        {
            Directory.CreateDirectory(interrupted);
            await File.WriteAllTextAsync(Path.Combine(interrupted, "partial.bin"), "partial");
            var manager = new OfflineMtAssetManager(finalDirectory);

            await manager.CleanupTemporaryDirectoriesAsync();

            Assert.False(Directory.Exists(interrupted));
            Assert.False(await manager.IsInstalledAsync());
        }
        finally
        {
            DeleteDirectory(parent);
        }
    }

    private static string Sha256(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "SubFlow-offline-mt-tests-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
