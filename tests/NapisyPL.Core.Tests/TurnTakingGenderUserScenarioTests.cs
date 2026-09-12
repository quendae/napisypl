using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class TurnTakingGenderUserScenarioTests
{
    [Fact]
    public async Task MaleSpeaker_WhenNextResponderIsEligibleFemale_UsesFemaleSecondPersonForm()
    {
        using var http = new HttpClient(new StubHandler("""
            {"choices":[{"message":{"content":"[{\"id\":1,\"find\":\"Zrobiłeś\",\"replace\":\"Zrobiłaś\",\"confidence\":0.99,\"target\":\"addressee\"}]"},"finish_reason":"stop"}]}
            """));
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3-1.7b");

        var source = new[]
        {
            Cue(1, 0.0, 1.0, "Did you do it?"),
            Cue(2, 1.2, 2.0, "Yes, I did.")
        };
        var translated = new[]
        {
            Cue(1, 0.0, 1.0, "Zrobiłeś to?"),
            Cue(2, 1.2, 2.0, "Tak, zrobiłam.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_MALE",
            [2] = "SPEAKER_FEMALE"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_MALE"] = new(SpeakerVoiceGender.Male, 0.97, 3),
            ["SPEAKER_FEMALE"] = new(SpeakerVoiceGender.Female, 0.96, 3)
        };

        var resolution = DialogueAddresseeResolver.ResolveDetailed(source, speakers, cueId: 1);
        var result = await service.ReviewAsync(
            source,
            translated,
            speakers,
            speakerGenderEvidence: evidence);

        Assert.True(resolution.IsResolved);
        Assert.Equal("SPEAKER_FEMALE", resolution.SpeakerId);
        Assert.Equal("next_turn_two_speaker", resolution.ReasonCode);
        Assert.Equal("Zrobiłaś to?", result[0].Text);
    }

    [Fact]
    public async Task MaleSpeaker_MonologueWithoutOtherResponder_DoesNotGuessFemaleAddressee()
    {
        using var http = new HttpClient(new StubHandler("""
            {"choices":[{"message":{"content":"[{\"id\":1,\"find\":\"Zrobiłeś\",\"replace\":\"Zrobiłaś\",\"confidence\":0.99,\"target\":\"addressee\"}]"},"finish_reason":"stop"}]}
            """));
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3-1.7b");

        var source = new[]
        {
            Cue(1, 0.0, 1.0, "Did you do it?"),
            Cue(2, 1.2, 2.0, "Come on, we have to go.")
        };
        var translated = new[]
        {
            Cue(1, 0.0, 1.0, "Zrobiłeś to?"),
            Cue(2, 1.2, 2.0, "Chodź, musimy iść.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_MALE",
            [2] = "SPEAKER_MALE"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_MALE"] = new(SpeakerVoiceGender.Male, 0.97, 3)
        };

        var resolution = DialogueAddresseeResolver.ResolveDetailed(source, speakers, cueId: 1);
        var result = await service.ReviewAsync(
            source,
            translated,
            speakers,
            speakerGenderEvidence: evidence);

        Assert.False(resolution.IsResolved);
        Assert.Equal("Zrobiłeś to?", result[0].Text);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
