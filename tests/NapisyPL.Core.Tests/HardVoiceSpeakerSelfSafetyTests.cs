using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class HardVoiceSpeakerSelfSafetyTests
{
    [Fact]
    public void Review_HardVoiceWeakDirectionalCue_DoesNotFlipSpeakerSelfForm()
    {
        var source = new[]
        {
            Cue(227, 0, 2, "Did two wills today."),
            Cue(228, 2.1, 4.0, "Two wills, and I started a living trust.")
        };
        var translated = new[]
        {
            Cue(227, 0, 2, "Zrobiłem dziś dwa testamenty."),
            Cue(228, 2.1, 4.0, "Dwa testamenty i założyłem fundusz powierniczy.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [227] = "SPEAKER_04",
            [228] = "SPEAKER_67"
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            // Mirrors the S01E05 failure: female wins directionally, but evidence is tiny.
            [227] = new(
                SpeakerVoiceGender.Unknown,
                0,
                0.010,
                2.136,
                SpeakerVoiceGender.Female,
                0.865),
            [228] = new(
                SpeakerVoiceGender.Unknown,
                0,
                0.026,
                3.404,
                SpeakerVoiceGender.Male,
                0.754)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            new Dictionary<string, SpeakerGenderEvidence>(),
            cueGender,
            hardVoiceTurnOnly: true);

        Assert.Equal("Zrobiłem dziś dwa testamenty.", result[0].Text);
    }

    [Fact]
    public void Review_HardVoiceReliableSpeakerGender_FixesSelfFormWhenLocalCueIsWeak()
    {
        var source = new[]
        {
            Cue(181, 0, 2, "And here I thought all lawyers were idiots."),
            Cue(182, 2.1, 4.0, "No, only half of us are idiots.")
        };
        var translated = new[]
        {
            Cue(181, 0, 2, "I tutaj myślałem, że wszyscy prawnicy są idiotami."),
            Cue(182, 2.1, 4.0, "Nie, tylko połowa z nas jest idiotami.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [181] = "SPEAKER_07",
            [182] = "SPEAKER_04"
        };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_07"] = new(SpeakerVoiceGender.Female, 0.938, 3)
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [181] = new(
                SpeakerVoiceGender.Unknown,
                0,
                0.006,
                2.837,
                SpeakerVoiceGender.Female,
                0.918),
            [182] = new(
                SpeakerVoiceGender.Unknown,
                0,
                0.016,
                3.738,
                SpeakerVoiceGender.Male,
                0.900)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerGender,
            cueGender,
            hardVoiceTurnOnly: true);

        Assert.Equal("I tutaj myślałam, że wszyscy prawnicy są idiotami.", result[0].Text);
    }

    [Fact]
    public void Review_SpeakerAgreement_DoesNotTreatPresentWysylamAsFemininePast()
    {
        var source = new[]
        {
            Cue(377, 0, 2, "And I'm not sending him to a rubber room.")
        };
        var translated = new[]
        {
            Cue(377, 0, 2, "I nie wysyłam go do gumowego pokoju.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [377] = "SPEAKER_JIMMY"
        };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_JIMMY"] = new(SpeakerVoiceGender.Male, 0.99, 3)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerGender);

        Assert.Equal("I nie wysyłam go do gumowego pokoju.", result[0].Text);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
