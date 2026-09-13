using System.Net;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class GptOssReviewCompatibilityTests
{
    [Fact]
    public async Task ReviewAsync_GptOssUsesPlainJsonObjectResponseFormat()
    {
        var handler = new RecordingHandler("""
            {"choices":[{"message":{"content":"{\"edits\":[]}"},"finish_reason":"stop"}]}
            """);
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "gpt-oss-20b");

        await service.ReviewAsync(
            new[] { Cue(1, "I was ready.") },
            new[] { Cue(1, "Byłem gotowy.") },
            new Dictionary<int, string?> { [1] = "SPEAKER_00" });

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        var responseFormat = body.RootElement.GetProperty("response_format");
        Assert.Equal("json_object", responseFormat.GetProperty("type").GetString());
        Assert.False(responseFormat.TryGetProperty("schema", out _));
    }

    [Fact]
    public async Task ReviewAsync_GptOssParsesWrappedEditsObject()
    {
        var handler = new RecordingHandler("""
            {"choices":[{"message":{"content":"{\"edits\":[{\"id\":1,\"find\":\"Byłem gotowy\",\"replace\":\"Byłam gotowa\",\"confidence\":0.98,\"target\":\"speaker\"}]}"},"finish_reason":"stop"}]}
            """);
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "gpt-oss-20b");
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Female, 0.98, 2)
        };

        var result = await service.ReviewAsync(
            new[] { Cue(1, "I was ready.") },
            new[] { Cue(1, "Byłem gotowy.") },
            new Dictionary<int, string?> { [1] = "SPEAKER_00" },
            speakerGenderEvidence: evidence);

        Assert.Equal("Byłam gotowa.", result[0].Text);
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class RecordingHandler(string response) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
