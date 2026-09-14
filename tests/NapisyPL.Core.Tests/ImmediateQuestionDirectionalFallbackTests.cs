using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class ImmediateQuestionDirectionalFallbackTests
{
    [Fact]
    public void Resolve_WhenImmediateAnswerHasWeakButHighlyDirectionalAudio_UsesDirectionalGender()
    {
        var cues = new[]
        {
            Cue(261, 18 * 60 + 51.756, 18 * 60 + 53.132, "Hold on. You got married?"),
            Cue(262, 18 * 60 + 53.340, 18 * 60 + 55.885, "No, no, no. My sister Angie's husband.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [261] = "SPEAKER_01",
            [262] = "SPEAKER_01"
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [261] = CueGenderEvidenceEvaluator.Evaluate(0.004, 0.001, 1.376),
            [262] = CueGenderEvidenceEvaluator.Evaluate(0.017, 0.001, 2.545)
        };

        Assert.Equal(SpeakerVoiceGender.Unknown, cueGender[262].Gender);

        var result = LocalTurnGenderResolver.Resolve(cues, speakers, cueGender, 261);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Male, result.Gender);
        Assert.Equal("next_cue_directional_question_answer", result.ReasonCode);
        Assert.True(result.Confidence >= 0.94);
    }

    [Fact]
    public void Review_WhenMarriageQuestionUsesWeakDirectionalMaleAnswer_UsesPolishMaleMarriageIdiom()
    {
        var source = new[]
        {
            Cue(261, 18 * 60 + 51.756, 18 * 60 + 53.132, "Hold on. You got married?"),
            Cue(262, 18 * 60 + 53.340, 18 * 60 + 55.885, "No, no, no. My sister Angie's husband.")
        };
        var translated = new[]
        {
            Cue(261, 18 * 60 + 51.756, 18 * 60 + 53.132, "Zatrzymaj się. Wyszłaś za mąż?"),
            Cue(262, 18 * 60 + 53.340, 18 * 60 + 55.885, "Nie, nie, nie. Męża mojej siostry Angie.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [261] = "SPEAKER_01",
            [262] = "SPEAKER_01"
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [261] = CueGenderEvidenceEvaluator.Evaluate(0.004, 0.001, 1.376),
            [262] = CueGenderEvidenceEvaluator.Evaluate(0.017, 0.001, 2.545)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            new Dictionary<string, SpeakerGenderEvidence>(),
            cueGender);

        Assert.Equal("Zatrzymaj się. Ożeniłeś się?", result[0].Text);
    }

    [Fact]
    public void Resolve_WhenWeakDirectionalNextCueFollowsStatement_DoesNotUseFallback()
    {
        var cues = new[]
        {
            Cue(625, 42 * 60 + 4.082, 42 * 60 + 8.627, "Kim, I can't imagine what you did to make that happen."),
            Cue(626, 42 * 60 + 8.836, 42 * 60 + 11.172, "I didn't do anything big.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [625] = "SPEAKER_01",
            [626] = "SPEAKER_04"
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [626] = CueGenderEvidenceEvaluator.Evaluate(0.003, 0.001, 2.336)
        };

        var result = LocalTurnGenderResolver.Resolve(cues, speakers, cueGender, 625);

        Assert.False(result.IsResolved);
        Assert.Equal("missing_next_gender", result.ReasonCode);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
