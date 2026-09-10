using System.Net;
using System.Text;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation.Providers;

namespace NapisyPL.Core.Tests;

public sealed class AnthropicProviderTests
{
    [Fact]
    public async Task TranslateAsync_UsesAnthropicApiKeyHeader()
    {
        var handler = new CapturingHandler();
        using var client = new HttpClient(handler);
        var provider = new AnthropicProvider(client, "test-key", "claude-test");

        var result = await provider.TranslateAsync([new TranslationSegment(1, "Hello")]);

        Assert.Equal("Cześć", result[1]);
        Assert.True(handler.Headers.TryGetValue("x-api-key", out var apiKey));
        Assert.Equal("test-key", apiKey);
        Assert.False(handler.Headers.ContainsKey("Authorization"));
        Assert.Equal("2023-06-01", handler.Headers["anthropic-version"]);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            foreach (var header in request.Headers)
                Headers[header.Key] = string.Join(",", header.Value);

            const string body = "{\"content\":[{\"type\":\"text\",\"text\":\"[{\\\"id\\\":1,\\\"text\\\":\\\"Cześć\\\"}]\"}]}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
