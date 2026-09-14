using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class HardVoiceTurnResolverTests
{
    [Fact]
    public void Resolve_MaleThenFemaleAt1000_TargetsFemale()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Male, 1.0, 1),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Female, 1.0, 1)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, evidence, 1);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Female, result.TargetGender);
        Assert.Equal("SPEAKER_01", result.TargetSpeakerId);
        Assert.Equal("next_speaker_opposite_gender_1000", result.ReasonCode);
    }

    [Fact]
    public void Resolve_FemaleThenMaleAt1000_TargetsMale()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Female, 1.0, 1),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Male, 1.0, 1)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, evidence, 1);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Male, result.TargetGender);
    }

    [Fact]
    public void Resolve_WhenEitherSpeakerIsBelowRounded1000_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Male, 0.9994, 3),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Female, 1.0, 3)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, evidence, 1);

        Assert.False(result.IsResolved);
        Assert.Equal("confidence_below_1000", result.ReasonCode);
    }

    [Fact]
    public void Resolve_WhenConsecutiveSpeakersHaveSameGender_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1, 2, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Male, 1.0, 2),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Male, 1.0, 2)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, evidence, 1);

        Assert.False(result.IsResolved);
        Assert.Equal("same_gender_turn", result.ReasonCode);
    }

    [Fact]
    public void Review_MaleThenFemaleAt1000_RewritesFemaleAddresseeAndMarksHardResolver()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "What did you do?"),
            Cue(2, 1, 2, "Nothing.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Co zrobiłeś?"),
            Cue(2, 1, 2, "Nic.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Male, 1.0, 1),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Female, 1.0, 1)
        };
        var diagnostics = new List<DeterministicGenderCueDiagnostic>();

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            evidence,
            diagnostics: diagnostics);

        Assert.Equal("Co zrobiłaś?", result[0].Text);
        var diagnostic = Assert.Single(diagnostics, item => item.CueId == 1);
        Assert.Equal("hard_voice_turn_1000", diagnostic.Resolver);
        Assert.Equal("next_speaker_opposite_gender_1000", diagnostic.ReasonCode);
        Assert.Equal(SpeakerVoiceGender.Female, diagnostic.TargetGender);
        Assert.True(diagnostic.GatePassed);
    }

    [Fact]
    public void Review_FemaleThenMaleAt1000_RewritesMaleAddresseeAndMarksHardResolver()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "What did you do?"),
            Cue(2, 1, 2, "Nothing.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Co zrobiłaś?"),
            Cue(2, 1, 2, "Nic.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_01"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Female, 1.0, 1),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Male, 1.0, 1)
        };
        var diagnostics = new List<DeterministicGenderCueDiagnostic>();

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            evidence,
            diagnostics: diagnostics);

        Assert.Equal("Co zrobiłeś?", result[0].Text);
        var diagnostic = Assert.Single(diagnostics, item => item.CueId == 1);
        Assert.Equal("hard_voice_turn_1000", diagnostic.Resolver);
        Assert.Equal(SpeakerVoiceGender.Male, diagnostic.TargetGender);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
