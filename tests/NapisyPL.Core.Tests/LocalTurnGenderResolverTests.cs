using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTurnGenderResolverTests
{
    [Fact]
    public void Resolve_WhenMaleCueIsImmediatelyAnsweredByFemale_UsesNextCueGenderWithoutGlobalSpeakerIdentity()
    {
        var cues = new[]
        {
            Cue(1, 0.0, 1.0, "Did you do it?"),
            Cue(2, 1.2, 2.0, "Yes.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_04",
            [2] = "SPEAKER_117"
        };
        var cueGender = Evidence(
            (1, SpeakerVoiceGender.Male),
            (2, SpeakerVoiceGender.Female));

        var result = LocalTurnGenderResolver.Resolve(cues, speakers, cueGender, 1);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Female, result.Gender);
        Assert.Equal("next_cue_gender_change", result.ReasonCode);
        Assert.True(result.Confidence >= 0.97);
    }

    [Fact]
    public void Resolve_WhenTwoMaleTurnsHaveDifferentLocalSpeakerIds_UsesNextMaleAsAddressee()
    {
        var cues = new[]
        {
            Cue(1, 0.0, 1.0, "You got married?"),
            Cue(2, 1.2, 2.0, "Yeah."),
            Cue(3, 2.1, 3.0, "Last year.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_04",
            [2] = "SPEAKER_07",
            [3] = "SPEAKER_07"
        };
        var cueGender = Evidence(
            (1, SpeakerVoiceGender.Male),
            (2, SpeakerVoiceGender.Male),
            (3, SpeakerVoiceGender.Male));

        var result = LocalTurnGenderResolver.Resolve(cues, speakers, cueGender, 1);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Male, result.Gender);
        Assert.Equal("next_cue_same_gender_stable_turn", result.ReasonCode);
    }

    [Fact]
    public void Resolve_WhenSameGenderSpeakerIdentityIsNotStable_DoesNotGuess()
    {
        var cues = new[]
        {
            Cue(1, 0.0, 1.0, "You got married?"),
            Cue(2, 1.2, 2.0, "Yeah."),
            Cue(3, 2.1, 3.0, "Last year.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_04",
            [2] = "SPEAKER_07",
            [3] = "SPEAKER_08"
        };
        var cueGender = Evidence(
            (1, SpeakerVoiceGender.Male),
            (2, SpeakerVoiceGender.Male),
            (3, SpeakerVoiceGender.Male));

        var result = LocalTurnGenderResolver.Resolve(cues, speakers, cueGender, 1);

        Assert.False(result.IsResolved);
        Assert.Equal("unstable_same_gender_turn", result.ReasonCode);
    }

    [Fact]
    public void Resolve_WhenNextCueIsTooFarAway_DoesNotGuess()
    {
        var cues = new[]
        {
            Cue(1, 0.0, 1.0, "Did you do it?"),
            Cue(2, 5.0, 6.0, "Yes.")
        };
        var cueGender = Evidence(
            (1, SpeakerVoiceGender.Male),
            (2, SpeakerVoiceGender.Female));

        var result = LocalTurnGenderResolver.Resolve(cues, new Dictionary<int, string?>(), cueGender, 1);

        Assert.False(result.IsResolved);
        Assert.Equal("next_cue_too_far", result.ReasonCode);
    }

    [Fact]
    public void Review_UsesLocalTurnGenderToFixMarriedExampleWhenGlobalSpeakerGenderIsUnavailable()
    {
        var source = new[]
        {
            Cue(1, 0.0, 1.0, "You got married?"),
            Cue(2, 1.2, 2.0, "Yeah."),
            Cue(3, 2.1, 3.0, "Last year.")
        };
        var translated = new[]
        {
            Cue(1, 0.0, 1.0, "Wyszłaś za mąż?"),
            Cue(2, 1.2, 2.0, "Tak."),
            Cue(3, 2.1, 3.0, "W zeszłym roku.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_04",
            [2] = "SPEAKER_07",
            [3] = "SPEAKER_07"
        };
        var cueGender = Evidence(
            (1, SpeakerVoiceGender.Male),
            (2, SpeakerVoiceGender.Male),
            (3, SpeakerVoiceGender.Male));

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            new Dictionary<string, SpeakerGenderEvidence>(),
            cueGender);

        Assert.Equal("Wyszedłeś za mąż?", result[0].Text);
    }

    [Theory]
    [InlineData(0.14, 0.01, 2.0, SpeakerVoiceGender.Male)]
    [InlineData(0.01, 0.13, 2.0, SpeakerVoiceGender.Female)]
    [InlineData(0.02, 0.018, 2.0, SpeakerVoiceGender.Unknown)]
    [InlineData(0.15, 0.01, 0.4, SpeakerVoiceGender.Unknown)]
    public void CueGenderEvidenceEvaluator_IsConservative(
        double male,
        double female,
        double duration,
        SpeakerVoiceGender expected)
    {
        var evidence = CueGenderEvidenceEvaluator.Evaluate(male, female, duration);
        Assert.Equal(expected, evidence.Gender);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static IReadOnlyDictionary<int, CueVoiceGenderEvidence> Evidence(
        params (int CueId, SpeakerVoiceGender Gender)[] items) =>
        items.ToDictionary(
            item => item.CueId,
            item => new CueVoiceGenderEvidence(item.Gender, 0.98, 0.10, 1.5));
}
