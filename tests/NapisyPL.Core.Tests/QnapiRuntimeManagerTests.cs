using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class QnapiRuntimeManagerTests
{
    [Fact]
    public async Task EnsureAvailableAsync_InstallsVerifiedArchiveAtomicallyAndWritesControlledConfig()
    {
        using var fixture = new RuntimeFixture();
        var archive = CreateArchive();
        var package = new QnapiRuntimePackage(
            "test", new Uri("https://example.invalid/qnapi.zip"),
            Convert.ToHexString(SHA256.HashData(archive)));
        using var http = new HttpClient(new BytesHandler(archive));
        var manager = new QnapiRuntimeManager(http, fixture.Root, package);

        var executable = await manager.EnsureAvailableAsync();

        Assert.True(File.Exists(executable));
        Assert.Equal(Path.Combine(fixture.Root, "test", "qnapi.exe"), executable);
        var config = await File.ReadAllTextAsync(Path.Combine(fixture.Root, "test", "qnapi.ini"));
        Assert.Contains("post_processing=true", config, StringComparison.Ordinal);
        Assert.Contains("quiet_batch=false", config, StringComparison.Ordinal);
        Assert.Contains("enc_to=UTF-8", config, StringComparison.Ordinal);
        Assert.Contains("search_policy=0", config, StringComparison.Ordinal);
        Assert.Contains("download_policy=0", config, StringComparison.Ordinal);
        Assert.Contains("OpenSubtitles:off", config, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateDirectories(fixture.Root, ".install-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task EnsureAvailableAsync_RejectsArchiveWhoseHashDoesNotMatch()
    {
        using var fixture = new RuntimeFixture();
        using var http = new HttpClient(new BytesHandler(CreateArchive()));
        var package = new QnapiRuntimePackage(
            "test", new Uri("https://example.invalid/qnapi.zip"), new string('0', 64));
        var manager = new QnapiRuntimeManager(http, fixture.Root, package);

        await Assert.ThrowsAsync<InvalidDataException>(() => manager.EnsureAvailableAsync());

        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "test")));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Root, ".install-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task EnsureAvailableAsync_DoesNotDownloadWhenCompleteRuntimeAlreadyExists()
    {
        using var fixture = new RuntimeFixture();
        var runtime = Path.Combine(fixture.Root, "test");
        WriteRequiredRuntimeFiles(runtime);
        using var http = new HttpClient(new ThrowingHandler());
        var package = new QnapiRuntimePackage(
            "test", new Uri("https://example.invalid/qnapi.zip"), new string('0', 64));
        var manager = new QnapiRuntimeManager(http, fixture.Root, package);

        var executable = await manager.EnsureAvailableAsync();

        Assert.Equal(Path.Combine(runtime, "qnapi.exe"), executable);
        Assert.True(File.Exists(Path.Combine(runtime, "qnapi.ini")));
    }

    private static byte[] CreateArchive()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in QnapiRuntimeManager.RequiredFiles)
            {
                var entry = zip.CreateEntry("QNapi/" + file);
                using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                writer.Write(file);
            }
        }
        return buffer.ToArray();
    }

    private static void WriteRequiredRuntimeFiles(string runtime)
    {
        Directory.CreateDirectory(runtime);
        foreach (var file in QnapiRuntimeManager.RequiredFiles)
        {
            var path = Path.Combine(runtime, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file);
        }
    }

    private sealed class RuntimeFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "NapisyPL-QnapiRuntimeTests-" + Guid.NewGuid().ToString("N"));

        public RuntimeFixture() => Directory.CreateDirectory(Root);
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The existing runtime should not be downloaded again.");
    }
}
