using System.IO.Compression;
using System.Text;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class OpusMarianAssetManagerTests
{
    [Fact]
    public void PinnedDescriptor_UsesExpectedEngPolRelease()
    {
        var descriptor = OpusMarianModelDescriptor.Pinned;

        Assert.Equal("opus-marian-eng-pol-2021-02-19", descriptor.BackendId);
        Assert.Equal("eng", descriptor.SourceLanguage);
        Assert.Equal("pol", descriptor.TargetLanguage);
        Assert.Equal("2021-02-19", descriptor.Release);
        Assert.Equal(
            "https://object.pouta.csc.fi/Tatoeba-MT-models/eng-pol/opus-2021-02-19.zip",
            descriptor.ArchiveUrl);
        Assert.Equal("normalization + SentencePiece spm32k/spm32k", descriptor.Preprocessing);
    }

    [Theory]
    [InlineData("../evil.bin")]
    [InlineData("nested/../../evil.bin")]
    [InlineData("/absolute.bin")]
    [InlineData("C:\\absolute.bin")]
    public void ResolveArchiveEntryPath_RejectsTraversalAndAbsolutePaths(string entryName)
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-opus-test-root");

        Assert.Throws<InvalidDataException>(() =>
            OpusMarianAssetManager.ResolveArchiveEntryPath(root, entryName));
    }

    [Fact]
    public void ResolveArchiveEntryPath_AllowsSafeNestedEntry()
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-opus-test-root");

        var resolved = OpusMarianAssetManager.ResolveArchiveEntryPath(root, "model/model.npz");

        Assert.StartsWith(Path.GetFullPath(root), resolved, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("model", "model.npz"), resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InstallPinnedModelAsync_ExtractsCanonicalModelFilesAndWritesBergamotConfig()
    {
        var root = TempDirectory();
        try
        {
            // The real 2021-02-19 archive does not call its model model.npz.
            var archive = Zip(
                ("opus.spm32k-spm32k.transformer.model1.npz.best-perplexity.npz", "MODEL"),
                ("source.spm", "SOURCE"),
                ("target.spm", "TARGET"),
                ("README.md", "ignore me"));
            var assets = new OfflineMtAssetManager(root);
            var manager = new OpusMarianAssetManager(
                assets,
                (url, cancellationToken) => Task.FromResult(archive));

            await manager.InstallPinnedModelAsync();

            Assert.True(await assets.IsInstalledAsync());
            Assert.Equal("MODEL", await File.ReadAllTextAsync(Path.Combine(root, "model.npz")));
            Assert.Equal("SOURCE", await File.ReadAllTextAsync(Path.Combine(root, "source.spm")));
            Assert.Equal("TARGET", await File.ReadAllTextAsync(Path.Combine(root, "target.spm")));
            Assert.False(File.Exists(Path.Combine(root, "README.md")));
            var config = await File.ReadAllTextAsync(Path.Combine(root, "config.yml"));
            Assert.Contains("model.npz", config, StringComparison.Ordinal);
            Assert.Contains("source.spm", config, StringComparison.Ordinal);
            Assert.Contains("target.spm", config, StringComparison.Ordinal);
            Assert.DoesNotContain("gemm-precision: int8", config, StringComparison.OrdinalIgnoreCase);
            var manifest = await File.ReadAllTextAsync(Path.Combine(root, "manifest.json"));
            Assert.Contains("\"licenseId\": \"Apache-2.0\"", manifest, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InstallPinnedModelAsync_WhenRequiredVocabIsMissing_DoesNotPromotePartialInstall()
    {
        var root = TempDirectory();
        try
        {
            var archive = Zip(
                ("opus.spm32k-spm32k.transformer.model1.npz.best-perplexity.npz", "MODEL"),
                ("source.spm", "SOURCE"));
            var assets = new OfflineMtAssetManager(root);
            var manager = new OpusMarianAssetManager(
                assets,
                (url, cancellationToken) => Task.FromResult(archive));

            var error = await Assert.ThrowsAsync<InvalidDataException>(
                () => manager.InstallPinnedModelAsync());

            Assert.Contains("target.spm", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(await assets.IsInstalledAsync());
            Assert.False(File.Exists(Path.Combine(root, "model.npz")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }
        return buffer.ToArray();
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "subflow-opus-installer-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
