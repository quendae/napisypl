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
    public void Profiles_ExposeFastBalancedAndQualityModelsWithPinnedLicenses()
    {
        Assert.Same(NllbModelDescriptor.Fast600M, NllbModelDescriptor.Pinned);

        var balanced = NllbModelDescriptor.Balanced1_3B;
        Assert.Equal("nllb-200-distilled-1.3b-eng-pol", balanced.BackendId);
        Assert.Equal("facebook/nllb-200-distilled-1.3B", balanced.ModelId);
        Assert.Equal("CC-BY-NC-4.0", balanced.LicenseId);
        Assert.True(balanced.BenchmarkOnly);
        Assert.Contains(balanced.Files, file => file.RelativePath == "pytorch_model.bin");

        var quality = NllbModelDescriptor.QualityMadlad3B;
        Assert.Equal("madlad-400-3b-eng-pol", quality.BackendId);
        Assert.Equal("google/madlad400-3b-mt", quality.ModelId);
        Assert.Equal("fa184c675da0b5c9e1c8694fccd4e12e2d422094", quality.Revision);
        Assert.Equal("en", quality.SourceLanguage);
        Assert.Equal("pl", quality.TargetLanguage);
        Assert.Equal("Apache-2.0", quality.LicenseId);
        Assert.False(quality.BenchmarkOnly);
        Assert.Contains(quality.Files, file => file.RelativePath == "model.safetensors");
        Assert.Contains(quality.Files, file => file.RelativePath == "spiece.model");

        foreach (var descriptor in new[] { NllbModelDescriptor.Fast600M, balanced, quality })
        {
            Assert.All(descriptor.Files, file =>
            {
                Assert.Contains(descriptor.Revision, file.DownloadUrl, StringComparison.Ordinal);
                Assert.DoesNotContain("/main/", file.DownloadUrl, StringComparison.Ordinal);
            });
        }
    }

    [Fact]
    public void OfflineMtPaths_ProvideDedicatedDirectoriesForAllProfiles()
    {
        var paths = new OfflineMtPaths("C:\\models");

        Assert.EndsWith(Path.Combine("models", "nllb-600m"), paths.Nllb600mDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("models", "nllb-1.3b"), paths.Nllb1_3bDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("models", "madlad-400-3b"), paths.Madlad3bDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(paths.Nllb600mDirectory, paths.GetNllbProfileDirectory(NllbModelProfile.Fast600M));
        Assert.Equal(paths.Nllb1_3bDirectory, paths.GetNllbProfileDirectory(NllbModelProfile.Balanced1_3B));
        Assert.Equal(paths.Madlad3bDirectory, paths.GetNllbProfileDirectory(NllbModelProfile.QualityMadlad3B));
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
