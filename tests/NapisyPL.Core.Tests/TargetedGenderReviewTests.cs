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
    public async Task ReviewAsync_IgnoresChangesOutsideCandidateIds()
    {
        var handler = new CountingHandler("""{"choices":[{"message":{"content":"[{\"id\":1,\"text\":\"Byłam gotowa.\"},{\"id\":2,\"text\":\"Nie powinno się zmienić\"}]"},"finish_reason":"stop"}]}""");
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");
        var source = new[] { Cue(1, "I was ready."), Cue(2, "Okay.") };
        var translated = new[] { Cue(1, "Byłem gotowy."), Cue(2, "Dobrze.") };

        var result = await service.ReviewAsync(
            source,
            translated,
            new Dictionary<int, string?> { [1] = "SPEAKER_00", [2] = "SPEAKER_01" });

        Assert.Equal("Byłam gotowa.", result[0].Text);
        Assert.Equal("Dobrze.", result[1].Text);
    }

    private static SubtitleCue Cue(int id, string text) => new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class CountingHandler(string response) : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
        }
    }
}
