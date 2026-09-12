using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class FirefoxBergamotTranslatorClientTests
{
    [Fact]
    public async Task EnsureReadyAsync_WhenValidModelIsInstalled_DoesNotRequireRemoteSettings()
    {
        var root = TempDirectory();
        try
        {
            var assets = new OfflineMtAssetManager(root);
            await InstallFixtureAsync(assets, modelVersion: "7.0");
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            var remoteCalls = 0;
            var installCalls = 0;
            await using var client = new FirefoxBergamotTranslatorClient(
                assets,
                runtime,
                (source, target, cancellationToken) =>
                {
                    remoteCalls++;
                    throw new InvalidOperationException("Remote Settings must not be required for a valid local install.");
                },
                (descriptor, cancellationToken) =>
                {
                    installCalls++;
                    return Task.CompletedTask;
                });

            await client.EnsureReadyAsync();
            var translated = await client.TranslateAsync(["hello"]);
            var info = client.GetInfo();

            Assert.Equal(0, remoteCalls);
            Assert.Equal(0, installCalls);
            Assert.Equal(new[] { "HELLO-PL" }, translated);
            Assert.Equal("bergamot-firefox-en-pl", info.BackendId);
            Assert.Equal("7.0", info.ModelVersion);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureReadyAsync_WhenModelIsMissing_ResolvesInstallsAndThenStartsRuntime()
    {
        var root = TempDirectory();
        try
        {
            var assets = new OfflineMtAssetManager(root);
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            var descriptor = Descriptor("8.0");
            var remoteCalls = 0;
            var installCalls = 0;
            await using var client = new FirefoxBergamotTranslatorClient(
                assets,
                runtime,
                (source, target, cancellationToken) =>
                {
                    remoteCalls++;
                    Assert.Equal("en", source);
                    Assert.Equal("pl", target);
                    return Task.FromResult(descriptor);
                },
                async (resolved, cancellationToken) =>
                {
                    installCalls++;
                    Assert.Same(descriptor, resolved);
                    await InstallFixtureAsync(assets, resolved.ModelVersion, cancellationToken);
                });

            await client.EnsureReadyAsync();
            await client.EnsureReadyAsync();
            var translated = await client.TranslateAsync(["hello"]);

            Assert.Equal(1, remoteCalls);
            Assert.Equal(1, installCalls);
            Assert.Equal(new[] { "HELLO-PL" }, translated);
            Assert.Single(channel.SentCommands.OfType<LoadCommand>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ProductionConstructor_WhenModelIsMissing_WiresRemoteSettingsAndAttachmentDownloads()
    {
        var root = TempDirectory();
        try
        {
            var model = Encoding.UTF8.GetBytes("fixture-firefox-model");
            var vocab = Encoding.UTF8.GetBytes("fixture-firefox-vocab");
            var lex = Encoding.UTF8.GetBytes("fixture-firefox-lex");
            var registry = RegistryJson(model, vocab, lex);
            using var http = new HttpClient(new FirefoxFixtureHandler(registry, model, vocab, lex));
            var assets = new OfflineMtAssetManager(root);
            var channel = new FakeChannel();
            await using var runtime = Runtime(root, channel);
            await using var client = new FirefoxBergamotTranslatorClient(http, assets, runtime);

            await client.EnsureReadyAsync();
            var translated = await client.TranslateAsync(["hello"]);

            Assert.True(await assets.IsInstalledAsync());
            Assert.Equal(new[] { "HELLO-PL" }, translated);
            Assert.Equal("9.0", client.GetInfo().ModelVersion);
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
        string modelVersion,
        CancellationToken cancellationToken = default)
    {
        var configBytes = Encoding.UTF8.GetBytes("models:\n  - model.bin\n");
        var modelBytes = Encoding.UTF8.GetBytes("fixture-model");
        var manifest = new OfflineMtManifest(
            BackendId: "bergamot-firefox-en-pl",
            EngineVersion: "0.4.5",
            ModelId: "firefox-translations-en-pl",
            ModelVersion: modelVersion,
            ModelSource: "fixture",
            LicenseId: "MPL-2.0",
            BenchmarkOnly: false,
            InstalledSizeBytes: configBytes.LongLength + modelBytes.LongLength,
            InstalledAtUtc: DateTimeOffset.UtcNow,
            Files:
            [
                ManifestFile("config.yml", configBytes),
                ManifestFile("model.bin", modelBytes)
            ]);

        await assets.InstallAsync(
            async (stagingDirectory, ct) =>
            {
                await System.IO.File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "config.yml"), configBytes, ct);
                await System.IO.File.WriteAllBytesAsync(Path.Combine(stagingDirectory, "model.bin"), modelBytes, ct);
                return manifest;
            },
            cancellationToken);
    }

    private static OfflineMtManifestFile ManifestFile(string path, byte[] bytes) =>
        new(path, Sha(bytes), bytes.LongLength);

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string RegistryJson(byte[] model, byte[] vocab, byte[] lex)
    {
        object Record(string fileType, string name, string location, byte[] bytes) => new
        {
            sourceLanguage = "en",
            targetLanguage = "pl",
            architecture = "base",
            version = "9.0",
            fileType,
            name,
            decompressedHash = Sha(bytes),
            decompressedSize = bytes.LongLength,
            attachment = new
            {
                hash = Sha(bytes),
                size = bytes.LongLength,
                filename = name,
                location
            }
        };

        return JsonSerializer.Serialize(new
        {
            changes = new[]
            {
                Record("model", "model.bin", "fixture/model.bin", model),
                Record("vocab", "vocab.spm", "fixture/vocab.spm", vocab),
                Record("lex", "lex.bin", "fixture/lex.bin", lex)
            }
        });
    }

    private static BergamotModelDescriptor Descriptor(string version)
    {
        static BergamotRemoteAsset Asset(string type, string fileName) => new()
        {
            FileType = type,
            FileName = fileName,
            Url = "https://example.invalid/" + fileName,
            Sha256 = new string('a', 64),
            SizeBytes = 1,
            DownloadSha256 = new string('b', 64),
            DownloadSizeBytes = 1,
            DownloadFileName = fileName,
            IsZstdCompressed = false
        };

        return new BergamotModelDescriptor
        {
            SourceLanguage = "en",
            TargetLanguage = "pl",
            ModelVersion = version,
            Model = Asset("model", "model.bin"),
            SourceVocab = Asset("srcvocab", "src.spm"),
            TargetVocab = Asset("trgvocab", "trg.spm"),
            Shortlist = Asset("lex", "lex.bin")
        };
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "subflow-firefox-client-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FirefoxFixtureHandler(
        string registryJson,
        byte[] model,
        byte[] vocab,
        byte[] lex) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.AbsoluteUri ?? string.Empty;
            if (uri.Contains("translations-models-v2/changeset", StringComparison.Ordinal))
                return Response(Encoding.UTF8.GetBytes(registryJson), "application/json");
            if (uri.EndsWith("/fixture/model.bin", StringComparison.Ordinal))
                return Response(model);
            if (uri.EndsWith("/fixture/vocab.spm", StringComparison.Ordinal))
                return Response(vocab);
            if (uri.EndsWith("/fixture/lex.bin", StringComparison.Ordinal))
                return Response(lex);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static Task<HttpResponseMessage> Response(byte[] body, string? contentType = null)
        {
            var content = new ByteArrayContent(body);
            if (contentType is not null)
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }

    private sealed class FakeChannel : IBergamotRuntimeChannel
    {
        private readonly Queue<LocalTranslatorEvent> _events = new();
        public List<LocalTranslatorCommand> SentCommands { get; } = [];
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 4321;

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
