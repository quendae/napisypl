using System.Security.Cryptography;
using System.Text;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class OpusMarianTranslatorClientTests
{
    [Fact]
    public async Task EnsureReadyAsync_WhenValidModelIsInstalled_DoesNotDownloadArchive()
    {
        var root = TempDirectory();
        try
        {
            var assets = new OfflineMtAssetManager(root);
            await InstallFixtureAsync(assets);
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            var installCalls = 0;
            await using var client = new OpusMarianTranslatorClient(
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
            Assert.Equal(new[] { "HELLO-PL" }, translated);
            Assert.Equal("opus-marian-eng-pol-2021-02-19", info.BackendId);
            Assert.Equal("2021-02-19", info.ModelVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenModelIsMissing_InstallsOnceAndStartsRuntimeOnce()
    {
        var root = TempDirectory();
        try
        {
            var assets = new OfflineMtAssetManager(root);
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            var installCalls = 0;
            await using var client = new OpusMarianTranslatorClient(
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
            Assert.Equal(new[] { "HELLO-PL" }, translated);
            Assert.Single(channel.SentCommands.OfType<LoadCommand>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static BergamotRuntimeManager Runtime(string modelDirectory, FakeChannel channel) =>
        new(
            new BergamotRuntimeOptions("fake-helper.exe", TimeSpan.FromSeconds(5)),
            modelDirectory,
            _ => channel);

    private static async Task InstallFixtureAsync(
        OfflineMtAssetManager assets,
        CancellationToken cancellationToken = default)
    {
        var modelBytes = Encoding.UTF8.GetBytes("MODEL");
        var sourceBytes = Encoding.UTF8.GetBytes("SOURCE");
        var targetBytes = Encoding.UTF8.GetBytes("TARGET");
        var configBytes = Encoding.UTF8.GetBytes(
            "models:\n  - 'model.npz'\nvocabs:\n  - 'source.spm'\n  - 'target.spm'\n");
        var manifest = new OfflineMtManifest(
            BackendId: "opus-marian-eng-pol-2021-02-19",
            EngineVersion: "bergamot-translator-0.4.5",
            ModelId: "opus-marian-eng-pol-2021-02-19",
            ModelVersion: "2021-02-19",
            ModelSource: "fixture",
            LicenseId: "NOASSERTION",
            BenchmarkOnly: true,
            InstalledSizeBytes: modelBytes.LongLength + sourceBytes.LongLength + targetBytes.LongLength + configBytes.LongLength,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            Files:
            [
                ManifestFile("model.npz", modelBytes),
                ManifestFile("source.spm", sourceBytes),
                ManifestFile("target.spm", targetBytes),
                ManifestFile("config.yml", configBytes)
            ]);

        await assets.InstallAsync(
            async (stagingDirectory, ct) =>
            {
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "model.npz"), modelBytes, ct);
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "source.spm"), sourceBytes, ct);
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "target.spm"), targetBytes, ct);
                await File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "config.yml"), configBytes, ct);
                return manifest;
            },
            cancellationToken);
    }

    private static OfflineMtManifestFile ManifestFile(string path, byte[] bytes) =>
        new(path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes.LongLength);

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "subflow-opus-client-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeChannel : IBergamotRuntimeChannel
    {
        private readonly Queue<LocalTranslatorEvent> _events = new();
        public List<LocalTranslatorCommand> SentCommands { get; } = [];
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 5432;

        public Task SendAsync(LocalTranslatorCommand command, CancellationToken cancellationToken = default)
        {
            SentCommands.Add(command);
            switch (command)
            {
                case LoadCommand:
                    _events.Enqueue(new ReadyEvent("fixture", "0.4.5"));
                    break;
                case TranslateCommand translate:
                    foreach (var segment in translate.Segments)
                        _events.Enqueue(new SegmentEvent(translate.JobId, segment.Id, segment.Text.ToUpperInvariant() + "-PL"));
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
