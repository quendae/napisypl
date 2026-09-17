using NapisyPL.Core.OfflineMt;

namespace NapisyPL.Core.Tests;

public sealed class OfflineMtManifestTests
{
    [Fact]
    public void NllbMetadata_CanRepresentBenchmarkOnlyLicense()
    {
        var info = new OfflineMachineTranslatorInfo(
            BackendId: "nllb-200-distilled-600m-eng-pol",
            DisplayName: "Local NLLB-600M (offline, benchmark)",
            ModelId: "facebook/nllb-200-distilled-600M",
            ModelVersion: "main",
            ModelSource: "https://huggingface.co/facebook/nllb-200-distilled-600M",
            ModelSha256: "abc123",
            InstalledSizeBytes: 2_500_000_000,
            RuntimeName: "transformers",
            RuntimeVersion: "4.x",
            Device: "cpu",
            LicenseId: "CC-BY-NC-4.0",
            BenchmarkOnly: true);

        Assert.True(info.BenchmarkOnly);
        Assert.Equal("CC-BY-NC-4.0", info.LicenseId);
        Assert.Equal("nllb-200-distilled-600m-eng-pol", info.BackendId);
    }

    [Fact]
    public void Manifest_PreservesRequiredFileMetadata()
    {
        var manifest = new OfflineMtManifest(
            BackendId: "bergamot-firefox-en-pl",
            EngineVersion: "0.4.5",
            ModelId: "enpl",
            ModelVersion: "base",
            ModelSource: "https://example.invalid/model",
            LicenseId: "MPL-2.0",
            BenchmarkOnly: false,
            InstalledSizeBytes: 1234,
            InstalledAtUtc: DateTimeOffset.UnixEpoch,
            Files:
            [
                new OfflineMtManifestFile("model/model.bin", "0123456789abcdef", 1234)
            ]);

        var file = Assert.Single(manifest.Files);
        Assert.Equal("model/model.bin", file.RelativePath);
        Assert.Equal("0123456789abcdef", file.Sha256);
        Assert.Equal(1234, file.SizeBytes);
    }
}
