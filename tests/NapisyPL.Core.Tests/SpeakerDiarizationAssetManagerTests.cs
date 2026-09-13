using System.Net;
using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerDiarizationAssetManagerTests
{
    [Fact]
    public async Task EnsureAvailableAsync_DownloadsAndAtomicallyRenamesModelsOnWindows()
    {
        var temp = Path.Combine(Path.GetTempPath(), "SubFlow-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            using var httpClient = new HttpClient(new StaticBytesHandler([1, 2, 3, 4]));
            var options = new SpeakerDiarizationOptions(
                temp,
                "https://example.test/segmentation.onnx",
                "https://example.test/embedding.onnx",
                0.65);
            var manager = new SpeakerDiarizationAssetManager(httpClient, options);

            await manager.EnsureAvailableAsync();

            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(options.SegmentationModelPath));
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(options.EmbeddingModelPath));
            Assert.False(File.Exists(options.SegmentationModelPath + ".partial"));
            Assert.False(File.Exists(options.EmbeddingModelPath + ".partial"));
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    private sealed class StaticBytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
    }
}
