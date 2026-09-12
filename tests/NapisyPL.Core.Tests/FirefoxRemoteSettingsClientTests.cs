using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class FirefoxRemoteSettingsClientTests
{
    [Fact]
    public async Task ResolveModelAsync_FetchesChangesetWithExpectedZeroAndSubFlowUserAgent()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new DelegateHandler(request =>
        {
            capturedRequest = request;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(RegistryJson(), Encoding.UTF8, "application/json")
            };
        });
        using var httpClient = new HttpClient(handler);
        var client = new FirefoxRemoteSettingsClient(
            httpClient,
            new Uri("https://settings.example.test/v2/"));

        var descriptor = await client.ResolveModelAsync("en", "pl");

        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "https://settings.example.test/v2/buckets/main/collections/translations-models-v2/changeset?_expected=0",
            capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Contains(capturedRequest.Headers.UserAgent, product =>
            string.Equals(product.Product?.Name, "SubFlow", StringComparison.Ordinal));
        Assert.Contains(capturedRequest.Headers.AcceptEncoding, encoding =>
            string.Equals(encoding.Value, "gzip", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("3.0", descriptor.ModelVersion);
        Assert.Equal("model.enpl.bin", descriptor.Model.FileName);
    }

    [Fact]
    public async Task ResolveModelAsync_DecodesGzipChangesetWithoutHandlerAutomaticDecompression()
    {
        var handler = new DelegateHandler(_ =>
        {
            var content = new ByteArrayContent(Gzip(Encoding.UTF8.GetBytes(RegistryJson())));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            content.Headers.ContentEncoding.Add("gzip");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
        });
        using var httpClient = new HttpClient(handler);
        var client = new FirefoxRemoteSettingsClient(
            httpClient,
            new Uri("https://settings.example.test/v2/"));

        var descriptor = await client.ResolveModelAsync("en", "pl");

        Assert.Equal("3.0", descriptor.ModelVersion);
        Assert.Equal("model.enpl.bin", descriptor.Model.FileName);
    }

    private static string RegistryJson() => """
        {
          "timestamp": 1774296704854,
          "changes": [
            {"name":"model.enpl.bin","sourceLanguage":"en","targetLanguage":"pl","architecture":"base","version":"3.0","fileType":"model","decompressedHash":"raw-model","decompressedSize":101,"filter_expression":"","attachment":{"hash":"zst-model","size":51,"location":"main-workspace/translations-models-v2/model.zst","filename":"model.zst"}},
            {"name":"vocab.enpl.spm","sourceLanguage":"en","targetLanguage":"pl","architecture":"base","version":"3.0","fileType":"vocab","decompressedHash":"raw-vocab","decompressedSize":102,"filter_expression":"","attachment":{"hash":"zst-vocab","size":52,"location":"main-workspace/translations-models-v2/vocab.zst","filename":"vocab.zst"}},
            {"name":"lex.enpl.bin","sourceLanguage":"en","targetLanguage":"pl","architecture":"base","version":"3.0","fileType":"lex","decompressedHash":"raw-lex","decompressedSize":103,"filter_expression":"","attachment":{"hash":"zst-lex","size":53,"location":"main-workspace/translations-models-v2/lex.zst","filename":"lex.zst"}}
          ]
        }
        """;

    private static byte[] Gzip(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(handler(request));
    }
}
