using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTargetedGenderReviewContinuationTests
{
    [Fact]
    public async Task ReviewAsync_WhenOneBatchIsInvalid_ContinuesWithLaterBatches()
    {
        var handler = new SequenceHandler(
            """
            {"choices":[{"message":{"content":"not-json"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":1}}
            """,
            """
            {"choices":[{"message":{"content":"[{\"id\":2,\"find\":\"Byłem\",\"replace\":\"Byłam\",\"confidence\":0.96,\"target\":\"speaker\"}]"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":10}}
            """);
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3-1.7b",
            maxCandidatesPerBatch: 1);
        var source = new[]
        {
            Cue(1, "I was ready."),
            Cue(2, "I was ready.")
        };
        var translated = new[]
        {
            Cue(1, "Byłem gotowy."),
            Cue(2, "Byłem gotowy.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = "SPEAKER_B"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_B"] = new(SpeakerVoiceGender.Female, 0.96, 2)
        };

        var result = await service.ReviewAsync(
            source,
            translated,
            speakers,
            speakerGenderEvidence: evidence);

        Assert.Equal(2, handler.RequestCount);
        Assert.Equal("Byłem gotowy.", result[0].Text);
        Assert.Equal("Byłam gotowy.", result[1].Text);
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id * 2), TimeSpan.FromSeconds(id * 2 + 1), text);

    private sealed class SequenceHandler(params string[] responses) : HttpMessageHandler
    {
        private int _index;
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            var response = responses[Math.Min(_index, responses.Length - 1)];
            _index++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
