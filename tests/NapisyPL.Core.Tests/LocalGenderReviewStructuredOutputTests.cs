using System.Net;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalGenderReviewStructuredOutputTests
{
    [Fact]
    public async Task ReviewAsync_SendsJsonSchemaAndDisablesReasoning()
    {
        var handler = new CapturingHandler("""{"choices":[{"message":{"content":"[]"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var service = new LocalGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b", windowSize: 40);
        var source = new[] { new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "I was ready.") };
        var translated = new[] { new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Byłem gotowy.") };
        var context = new ContextMap(
            new Dictionary<string, SpeakerContext> { ["SPEAKER_00"] = new(SpeakerGender.Male, 0.95) },
            new Dictionary<int, LineContext>());
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_00" };

        await service.ReviewAsync(source, translated, context, speakers);

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        var responseFormat = request.RootElement.GetProperty("response_format");
        Assert.Equal("json_schema", responseFormat.GetProperty("type").GetString());
        Assert.Equal("array", responseFormat.GetProperty("schema").GetProperty("type").GetString());
        Assert.Equal("none", request.RootElement.GetProperty("reasoning_effort").GetString());
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
