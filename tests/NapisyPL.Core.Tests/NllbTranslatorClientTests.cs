using System.Security.Cryptography;
using System.Text;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Nllb;

namespace NapisyPL.Core.Tests;

public sealed class NllbTranslatorClientTests
{
    [Fact]
    public async Task EnsureReadyAsync_WhenValidModelIsInstalled_DoesNotInstallAndReportsBenchmarkLicense()
    {
        var root = TempDirectory();
        try
        {
            var assets = new OfflineMtAssetManager(root);
            await InstallFixtureAsync(assets);
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            var installCalls = 0;
            await using var client = new NllbTranslatorClient(
                assets,
                runtime,
                cancellationToken =>
                {
                    installCalls++;
                    return Task.CompletedTask;
                });

            await client.EnsureReadyAsync();
            var translated = await client.TranslateAsync(["hello"]);
            var info = client.GetInfo();

            Assert.Equal(0, installCalls);
            Assert.Equal(["PL:hello"], translated);
            Assert.Equal("nllb-200-distilled-600m-eng-pol", info.BackendId);
            Assert.Equal("facebook/nllb-200-distilled-600M", info.ModelId);
            Assert.Equal(NllbModelDescriptor.Pinned.Revision, info.ModelVersion);
            Assert.Equal("transformers", info.RuntimeName);
            Assert.Equal("transformers-4.57.6", info.RuntimeVersion);
            Assert.Equal("CC-BY-NC-4.0", info.LicenseId);
            Assert.True(info.BenchmarkOnly);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenModelIsMissing_InstallsExactlyOnce()
    {
        var root = TempDirectory();
        try
        {
            var assets = new OfflineMtAssetManager(root);
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            var installCalls = 0;
            await using var client = new NllbTranslatorClient(
                assets,
                runtime,
                async cancellationToken =>
                {
                    installCalls++;
                    await InstallFixtureAsync(assets, cancellationToken);
                });

            await client.EnsureReadyAsync();
            await client.EnsureReadyAsync();
            var translated = await client.TranslateAsync(["hello"]);

            Assert.Equal(1, installCalls);
            Assert.Equal(["PL:hello"], translated);
            Assert.Single(channel.SentCommands.OfType<LoadCommand>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static NllbRuntimeManager Runtime(string modelDirectory, FakeChannel channel) =>
        new(
            new NllbRuntimeOptions("python.exe", "nllb_helper.py", TimeSpan.FromSeconds(5)),
            modelDirectory,
            _ => channel);

    private static async Task InstallFixtureAsync(
        OfflineMtAssetManager assets,
        CancellationToken cancellationToken = default)
    {
        var modelBytes = Encoding.UTF8.GetBytes("MODEL");
        var configBytes = Encoding.UTF8.GetBytes("{}");
        var manifest = new OfflineMtManifest(
            BackendId: "nllb-200-distilled-600m-eng-pol",
            EngineVersion: "transformers-4.57.6",
            ModelId: "facebook/nllb-200-distilled-600M",
            ModelVersion: NllbModelDescriptor.Pinned.Revision,
            ModelSource: NllbModelDescriptor.Pinned.RepositoryTreeUrl,
            LicenseId: "CC-BY-NC-4.0",
            BenchmarkOnly: true,
            InstalledSizeBytes: modelBytes.LongLength + configBytes.LongLength,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            Files:
            [
                ManifestFile("pytorch_model.bin", modelBytes),
                ManifestFile("config.json", configBytes)
            ]);

        await assets.InstallAsync(
            async (stagingDirectory, ct) =>
            {
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "pytorch_model.bin"), modelBytes, ct);
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "config.json"), configBytes, ct);
                return manifest;
            },
            cancellationToken);
    }

    private static OfflineMtManifestFile ManifestFile(string path, byte[] bytes) =>
        new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "subflow-nllb-client-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeChannel : INllbRuntimeChannel
    {
        private readonly Queue<LocalTranslatorEvent> _events = new();
        public List<LocalTranslatorCommand> SentCommands { get; } = [];
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 9876;

        public Task SendAsync(LocalTranslatorCommand command, CancellationToken cancellationToken = default)
        {
            SentCommands.Add(command);
            switch (command)
            {
                case LoadCommand:
                    _events.Enqueue(new ReadyEvent("fixture-nllb", "transformers-4.57.6"));
                    break;
                case TranslateCommand translate:
                    foreach (var segment in translate.Segments)
                        _events.Enqueue(new SegmentEvent(translate.JobId, segment.Id, "PL:" + segment.Text));
                    _events.Enqueue(new CompleteEvent(translate.JobId));
                    break;
            }
            return Task.CompletedTask;
        }

        public Task<LocalTranslatorEvent> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_events.Dequeue());

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
