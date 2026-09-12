using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTargetedGenderReviewFailureTests
{
    [Fact]
    public async Task ReviewAsync_WhenReviewerReturnsInvalidContent_KeepsBaseTranslation()
    {
        using var http = new HttpClient(new StubHandler("""
            {"choices":[{"finish_reason":"stop","message":{"content":"not-json"}}]}
            """));
        var service = new LocalTargetedGenderReviewService(http, "http://localhost:17843/v1", "qwen3-1.7b");
        var source = new[] { Cue(1, "I was ready.") };
        var translated = new[] { Cue(1, "Byłem gotowy.") };
        var statusMessages = new List<string>();

        var result = await service.ReviewAsync(
            source,
            translated,
            new Dictionary<int, string?> { [1] = "SPEAKER_00" },
            status: new InlineProgress<string>(statusMessages.Add));

        Assert.Equal("Byłem gotowy.", result.Single().Text);
        Assert.Contains(statusMessages, message => message.Contains("zachowuję", StringComparison.OrdinalIgnoreCase));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
