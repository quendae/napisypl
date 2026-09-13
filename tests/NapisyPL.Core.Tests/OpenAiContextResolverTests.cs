using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class OpenAiContextResolverTests
{
    [Fact]
    public async Task ResolveAsync_UsesChatCompletionsAndParsesContextMetadata()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var resolver = new OpenAiContextResolver(client, "http://127.0.0.1:8080/v1", "qwen3-1.7b");

        var result = await resolver.ResolveAsync([
            new ContextCue(118, "SPEAKER_01", "Are you ready?", null),
            new ContextCue(119, "SPEAKER_02", "Yes, Sarah.", "John")
        ]);

        Assert.Equal("http://127.0.0.1:8080/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.Contains("/no_think", handler.RequestBody);
        Assert.Contains("qwen3-1.7b", handler.RequestBody);
        Assert.Equal(SpeakerGender.Female, result.Speakers["SPEAKER_01"].Gender);
        Assert.Equal("SPEAKER_01", result.Lines[119].Addressee);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            const string metadata = "{\"speakers\":{\"SPEAKER_01\":{\"gender\":\"female\",\"confidence\":0.95}},\"lines\":{\"119\":{\"addressee\":\"SPEAKER_01\",\"confidence\":0.9}}}";
            var escaped = metadata.Replace("\\", "\\\\").Replace("\"", "\\\"");
            var body = "{\"choices\":[{\"message\":{\"content\":\"" + escaped + "\"}}]}";

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}
