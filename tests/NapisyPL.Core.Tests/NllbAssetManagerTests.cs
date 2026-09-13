using System.Text;
using System.Text.Json;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Nllb;

namespace NapisyPL.Core.Tests;

public sealed class NllbAssetManagerTests
{
    [Fact]
    public void PinnedDescriptor_IsBenchmarkOnlyEnglishToPolishAndImmutableRevision()
    {
        var descriptor = NllbModelDescriptor.Pinned;

        Assert.Equal("nllb-200-distilled-600m-eng-pol", descriptor.BackendId);
        Assert.Equal("facebook/nllb-200-distilled-600M", descriptor.ModelId);
        Assert.Equal("f8d333a098d19b4fd9a8b18f94170487ad3f821d", descriptor.Revision);
        Assert.Equal("eng_Latn", descriptor.SourceLanguage);
        Assert.Equal("pol_Latn", descriptor.TargetLanguage);
        Assert.Equal("CC-BY-NC-4.0", descriptor.LicenseId);
        Assert.True(descriptor.BenchmarkOnly);
        Assert.Contains(descriptor.Files, file => file.RelativePath == "pytorch_model.bin");
        Assert.Contains(descriptor.Files, file => file.RelativePath == "sentencepiece.bpe.model");
        Assert.All(descriptor.Files, file =>
        {
            Assert.Contains(descriptor.Revision, file.DownloadUrl, StringComparison.Ordinal);
            Assert.DoesNotContain("/main/", file.DownloadUrl, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task InstallPinnedModelAsync_DownloadsToAtomicAssetDirectoryAndWritesBenchmarkManifest()
    {
        var root = TempDirectory();
        try
        {
            var descriptor = NllbModelDescriptor.Pinned;
            var assets = new OfflineMtAssetManager(Path.Combine(root, "nllb-600m"));
            var requestedUrls = new List<string>();
            var manager = new NllbAssetManager(
                assets,
                async (url, destinationPath, cancellationToken) =>
                {
                    requestedUrls.Add(url);
                    var file = descriptor.Files.Single(item => item.DownloadUrl == url);
                    var bytes = Encoding.UTF8.GetBytes($"fixture:{file.RelativePath}");
                    await File.WriteAllBytesAsync(destinationPath, bytes, cancellationToken);
                });

            await manager.InstallPinnedModelAsync();

            Assert.True(await assets.IsInstalledAsync());
            Assert.Equal(descriptor.Files.Select(file => file.DownloadUrl), requestedUrls);

            var manifestJson = await File.ReadAllTextAsync(assets.ManifestPath);
            var manifest = JsonSerializer.Deserialize<OfflineMtManifest>(
                manifestJson,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(manifest);
            Assert.Equal(descriptor.BackendId, manifest.BackendId);
            Assert.Equal(descriptor.ModelId, manifest.ModelId);
            Assert.Equal(descriptor.Revision, manifest.ModelVersion);
            Assert.Contains(descriptor.Revision, manifest.ModelSource, StringComparison.Ordinal);
            Assert.Equal("transformers-4.57.6", manifest.EngineVersion);
            Assert.Equal("CC-BY-NC-4.0", manifest.LicenseId);
            Assert.True(manifest.BenchmarkOnly);
            Assert.Equal(descriptor.Files.Count, manifest.Files.Count);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "subflow-nllb-assets", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
