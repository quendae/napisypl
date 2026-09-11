using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTargetedGenderReviewAddresseeTests
{
    [Fact]
    public async Task ReviewAsync_WhenAddresseeIsAmbiguous_DropsSecondPersonGenderEdit()
    {
        using var http = new HttpClient(new StubHandler("""
            {"choices":[{"message":{"content":"[{\"id\":2,\"find\":\"zrobiłeś\",\"replace\":\"zrobiłaś\",\"confidence\":0.99,\"target\":\"addressee\"}]"},"finish_reason":"stop"}]}
            """));
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");
        var source = new[]
        {
            Cue(1, 0, 1, "Previous speaker."),
            Cue(2, 1.2, 2.2, "For what you did..."),
            Cue(3, 2.4, 3.4, "Another speaker.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Poprzednia kwestia."),
            Cue(2, 1.2, 2.2, "Za to, co zrobiłeś..."),
            Cue(3, 2.4, 3.4, "Kolejna kwestia.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_C"
        };

        var result = await service.ReviewAsync(source, translated, speakers);

        Assert.Equal("Za to, co zrobiłeś...", result[1].Text);
    }

    [Fact]
    public async Task ReviewAsync_WhenTurnTakingHasOneStrongAddressee_AllowsHighConfidenceSecondPersonEdit()
    {
        using var http = new HttpClient(new StubHandler("""
            {"choices":[{"message":{"content":"[{\"id\":2,\"find\":\"zrobiłeś\",\"replace\":\"zrobiłaś\",\"confidence\":0.99,\"target\":\"addressee\"}]"},"finish_reason":"stop"}]}
            """));
        var service = new LocalTargetedGenderReviewService(http, "http://127.0.0.1:17843/v1", "qwen3-1.7b");
        var source = new[]
        {
            Cue(1, 0, 1, "Previous speaker."),
            Cue(2, 1.2, 2.2, "For what you did..."),
            Cue(3, 2.4, 3.4, "Same other speaker.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Poprzednia kwestia."),
            Cue(2, 1.2, 2.2, "Za to, co zrobiłeś..."),
            Cue(3, 2.4, 3.4, "Kolejna kwestia.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_B"
        };

        var result = await service.ReviewAsync(source, translated, speakers);

        Assert.Equal("Za to, co zrobiłaś...", result[1].Text);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
