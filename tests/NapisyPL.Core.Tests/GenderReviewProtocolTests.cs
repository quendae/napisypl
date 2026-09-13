using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class GenderReviewProtocolTests
{
    [Fact]
    public void ParseResponse_AcceptsOnlyChangedCueTexts()
    {
        var result = GenderReviewProtocol.ParseResponse("[{\"id\":12,\"text\":\"Byłam gotowa.\"}]");

        Assert.Single(result);
        Assert.Equal("Byłam gotowa.", result[12]);
    }

    [Fact]
    public void ParseResponse_RejectsDuplicateIds()
    {
        const string json = "[{\"id\":12,\"text\":\"A\"},{\"id\":12,\"text\":\"B\"}]";
        Assert.Throws<InvalidDataException>(() => GenderReviewProtocol.ParseResponse(json));
    }

    [Fact]
    public void BuildPrompt_InstructsModelNotToRestyleCorrectLines()
    {
        var source = new SubtitleCue(12, TimeSpan.Zero, TimeSpan.FromSeconds(2), "I was ready.");
        var translated = source with { Text = "Byłem gotowy." };
        var context = new ContextMap(
            new Dictionary<string, SpeakerContext>
            {
                ["SPEAKER_01"] = new(SpeakerGender.Female, 0.96)
            },
            new Dictionary<int, LineContext>());
        var speakers = new Dictionary<int, string?> { [12] = "SPEAKER_01" };

        var prompt = GenderReviewProtocol.BuildPrompt([source], [translated], context, speakers);

        Assert.Contains("/no_think", prompt);
        Assert.Contains("Byłem gotowy.", prompt);
        Assert.Contains("female", prompt);
        Assert.Contains("only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("gender", prompt, StringComparison.OrdinalIgnoreCase);
    }
}
