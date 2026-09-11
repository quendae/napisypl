using System.Net;
using System.Text;
using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.Tests;

public sealed class ArgosModelAssetManagerTests
{
    [Fact]
    public async Task EnsureModelAsync_ClosesPartialBeforeAtomicMove()
    {
        var root = Path.Combine(Path.GetTempPath(), "subflow-argos-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Encoding.UTF8.GetBytes("fake-argos-model");
            using var http = new HttpClient(new BytesHandler(bytes));
            var options = new ArgosRuntimeOptions(root, "https://example.test/model.argosmodel", "model.argosmodel");
            var manager = new ArgosModelAssetManager(http, options);

            var path = await manager.EnsureModelAsync();

            Assert.Equal(options.ModelPath, path);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.False(File.Exists(path + ".partial"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            };
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }
}
