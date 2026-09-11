using System.Net;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation.Providers;

namespace NapisyPL.Core.Tests;

public sealed class LocalQwenStructuredOutputTests
{
    [Fact]
    public async Task ContextResolver_SendsJsonSchemaResponseFormat()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"{\"speakers\":{},\"lines\":{}}"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var resolver = new OpenAiContextResolver(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");

        await resolver.ResolveAsync([new ContextCue(1, "SPEAKER_00", "Are you ready?", null)]);

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        var responseFormat = request.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        Assert.Equal("subflow_context", responseFormat.GetProperty("json_schema").GetProperty("name").GetString());
        Assert.Equal(0, request.RootElement.GetProperty("temperature").GetDouble());
    }

    [Fact]
    public void ContextResolverProtocol_ExtractsJsonObjectFromHarmlessWrapperText()
    {
        var result = ContextResolverProtocol.ParseResponse("Result:\n{\"speakers\":{},\"lines\":{}}\nDone.");

        Assert.Empty(result.Speakers);
        Assert.Empty(result.Lines);
    }

    [Fact]
    public async Task LocalQwenProvider_UsesStrictJsonAndSmallLocalBatches()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"[{\"id\":7,\"text\":\"Jesteś gotowa?\"}]"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var provider = new LocalQwenProvider(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");

        var translated = await provider.TranslateAsync([new TranslationSegment(7, "Are you ready?")]);

        Assert.Equal("Jesteś gotowa?", translated[7]);
        Assert.Equal(10, provider.BatchPolicy.MaxSegments);
        Assert.Equal(3500, provider.BatchPolicy.MaxCharacters);

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        var responseFormat = request.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        Assert.Equal("subflow_translation", responseFormat.GetProperty("json_schema").GetProperty("name").GetString());
        Assert.Equal(0, request.RootElement.GetProperty("temperature").GetDouble());
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
