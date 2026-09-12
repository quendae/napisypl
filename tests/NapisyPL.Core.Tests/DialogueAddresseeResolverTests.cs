using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class DialogueAddresseeResolverTests
{
    [Fact]
    public void ResolveDetailed_WhenSameOtherSpeakerSurroundsCue_ReturnsHighConfidenceSandwich()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "B"),
            Cue(2, 1.2, 2.2, "A"),
            Cue(3, 2.4, 3.4, "B again")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_B"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 2);

        Assert.Equal("SPEAKER_B", result.SpeakerId);
        Assert.Equal(0.99, result.Confidence, 3);
        Assert.Equal("sandwich_turn", result.ReasonCode);
        Assert.Equal("SPEAKER_B", DialogueAddresseeResolver.Resolve(cues, speakers, 2));
    }

    [Fact]
    public void ResolveDetailed_WhenDifferentSpeakersSurroundCue_ReturnsUnresolved()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "B"),
            Cue(2, 1.2, 2.2, "A"),
            Cue(3, 2.4, 3.4, "C")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_C"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 2);

        Assert.Null(result.SpeakerId);
        Assert.Equal("third_speaker", result.ReasonCode);
    }

    [Fact]
    public void ResolveDetailed_WhenNextSpeakerRepliesInTwoPersonWindow_UsesNextSpeaker()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A asks"),
            Cue(2, 1.2, 2.2, "B replies")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = "SPEAKER_B"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 1);

        Assert.Equal("SPEAKER_B", result.SpeakerId);
        Assert.Equal(0.94, result.Confidence, 3);
        Assert.Equal("next_turn_two_speaker", result.ReasonCode);
    }

    [Fact]
    public void ResolveDetailed_WhenSpeakerUsesSeveralCuesBeforeReply_UsesReplyingSpeakerForWholeTurn()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A part one"),
            Cue(2, 1.1, 2.0, "A part two"),
            Cue(3, 2.1, 3.0, "A part three"),
            Cue(4, 3.2, 4.0, "B replies")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_A",
            [4] = "SPEAKER_B"
        };

        Assert.Equal("SPEAKER_B", DialogueAddresseeResolver.Resolve(cues, speakers, 1));
        Assert.Equal("SPEAKER_B", DialogueAddresseeResolver.Resolve(cues, speakers, 2));
        Assert.Equal("SPEAKER_B", DialogueAddresseeResolver.Resolve(cues, speakers, 3));
    }

    [Fact]
    public void ResolveDetailed_WhenRecentDialogueAlternatesAndNoReplyFollows_UsesPersistentPartner()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "B"),
            Cue(2, 1.1, 2.0, "A"),
            Cue(3, 2.1, 3.0, "B"),
            Cue(4, 3.1, 4.0, "A candidate")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_B",
            [4] = "SPEAKER_A"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 4);

        Assert.Equal("SPEAKER_B", result.SpeakerId);
        Assert.Equal(0.90, result.Confidence, 3);
        Assert.Equal("persistent_partner", result.ReasonCode);
    }

    [Fact]
    public void ResolveDetailed_WhenThirdSpeakerIsInsideLocalWindow_ReturnsUnresolved()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Group speaker C"),
            Cue(2, 1.1, 2.0, "Speaker B"),
            Cue(3, 2.1, 3.0, "Candidate A"),
            Cue(4, 3.1, 4.0, "Speaker B again"),
            Cue(5, 4.1, 5.0, "Group speaker C again")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_C",
            [2] = "SPEAKER_B",
            [3] = "SPEAKER_A",
            [4] = "SPEAKER_B",
            [5] = "SPEAKER_C"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 3);

        Assert.Null(result.SpeakerId);
        Assert.Equal("third_speaker", result.ReasonCode);
    }

    [Fact]
    public void ResolveDetailed_WhenReplyComesAfterLongGap_DoesNotCarryAddresseeAcrossSceneBreak()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A"),
            Cue(2, 12, 13, "B much later")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = "SPEAKER_B"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 1);

        Assert.Null(result.SpeakerId);
        Assert.Equal("insufficient_turn_evidence", result.ReasonCode);
    }

    [Fact]
    public void ResolveDetailed_WhenLocalSpeakerIdentityIsMissing_ReturnsUnresolved()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "A"),
            Cue(2, 1.2, 2.2, "Unknown"),
            Cue(3, 2.4, 3.4, "B")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = null,
            [3] = "SPEAKER_B"
        };

        var result = DialogueAddresseeResolver.ResolveDetailed(cues, speakers, 1);

        Assert.Null(result.SpeakerId);
        Assert.Equal("missing_local_speaker", result.ReasonCode);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
