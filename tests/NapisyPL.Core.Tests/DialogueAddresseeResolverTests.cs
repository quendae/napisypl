using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class DialogueAddresseeResolverTests
{
    [Fact]
    public void Resolve_WhenSameOtherSpeakerSurroundsCue_ReturnsThatSpeaker()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A"),
            Cue(2, 1.2, 2.2, "B"),
            Cue(3, 2.4, 3.4, "C")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_B"
        };

        Assert.Equal("SPEAKER_B", DialogueAddresseeResolver.Resolve(cues, speakers, 2));
    }

    [Fact]
    public void Resolve_WhenDifferentSpeakersSurroundCue_ReturnsNull()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A"),
            Cue(2, 1.2, 2.2, "B"),
            Cue(3, 2.4, 3.4, "C")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_C"
        };

        Assert.Null(DialogueAddresseeResolver.Resolve(cues, speakers, 2));
    }

    [Fact]
    public void Resolve_WhenOnlyOneSideHasAnotherSpeaker_ReturnsNull()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A"),
            Cue(2, 1.2, 2.2, "B")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A"
        };

        Assert.Null(DialogueAddresseeResolver.Resolve(cues, speakers, 2));
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
