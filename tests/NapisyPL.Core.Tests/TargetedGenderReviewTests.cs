using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class TargetedGenderReviewTests
{
    [Fact]
    public async Task ReviewAsync_DoesNotCallModelWhenThereAreNoCandidates()
    {
        var handler = new CountingHandler("""{"choices":[{"message":{"content":"[]"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");
        var source = new[] { Cue(1, "Okay.") };
        var translated = new[] { Cue(1, "Dobrze.") };

        var result = await service.ReviewAsync(source, translated, new Dictionary<int, string?>());

        Assert.Equal("Dobrze.", result[0].Text);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ReviewAsync_AppliesOnlySurgicalEditsInsideCandidateIds()
    {
        var handler = new CountingHandler("""{"choices":[{"message":{"content":"[{\"id\":1,\"find\":\"Byłem gotowy\",\"replace\":\"Byłam gotowa\",\"confidence\":0.97,\"target\":\"speaker\"},{\"id\":2,\"find\":\"Dobrze\",\"replace\":\"Źle\",\"confidence\":0.99,\"target\":\"speaker\"}]"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");
        var source = new[] { Cue(1, "I was ready."), Cue(2, "Okay.") };
        var translated = new[] { Cue(1, "Byłem gotowy."), Cue(2, "Dobrze.") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_00", [2] = "SPEAKER_01" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Female, 0.97, 2)
        };

        var result = await service.ReviewAsync(
            source,
            translated,
            speakers,
            speakerGenderEvidence: evidence);

        Assert.Equal("Byłam gotowa.", result[0].Text);
        Assert.Equal("Dobrze.", result[1].Text);
    }

    [Fact]
    public async Task ReviewAsync_IncludesOnlyCandidateSpeakerSamplesUsedByCurrentBatch()
    {
        var handler = new CountingHandler("""{"choices":[{"message":{"content":"[]"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");
        var source = Enumerable.Range(1, 10).Select(i => Cue(i, $"Source line {i}.")).ToArray();
        var translated = Enumerable.Range(1, 10)
            .Select(i => Cue(i, i == 5 ? "Byłem gotowy." : $"Linia {i}."))
            .ToArray();
        var speakers = new Dictionary<int, string?>
        {
            [4] = "SPEAKER_CONTEXT",
            [5] = "SPEAKER_CANDIDATE",
            [10] = "SPEAKER_OUTSIDE"
        };

        await service.ReviewAsync(source, translated, speakers);

        Assert.Contains("SPEAKER_CANDIDATE", handler.LastRequestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SPEAKER_OUTSIDE", handler.LastRequestBody, StringComparison.Ordinal);
    }

    private static SubtitleCue Cue(int id, string text) => new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class CountingHandler(string response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string LastRequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }
}
