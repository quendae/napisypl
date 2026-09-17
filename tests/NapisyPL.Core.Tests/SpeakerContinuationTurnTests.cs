using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// Cases taken from the S01E08 end-to-end run.
/// </summary>
public class SpeakerContinuationTurnTests
{
    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static CueVoiceGenderEvidence Accepted(SpeakerVoiceGender gender, double confidence) =>
        new(gender, confidence, 0.30, 1.5, gender, confidence);

    [Fact]
    public void QuestionSpreadOverTwoCues_TakesTheGenderOfWhoeverAnswers()
    {
        // 307 Gamby: "Did you or did you not make love to him?"
        // 308 Gamby: "Were you not lovers with Bill Hayden?"
        // 309 Amanda answers.
        var cues = new[]
        {
            Cue(307, 0, 2, "Did you or did you not make love to him?"),
            Cue(308, 2.2, 4, "Were you not lovers with Bill Hayden?"),
            Cue(309, 4.3, 6, "Are you asking if me and Bill Hayden fucked?")
        };
        var speakers = new Dictionary<int, string?> { [307] = "SPEAKER_02", [308] = "SPEAKER_02", [309] = "SPEAKER_05" };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [307] = Accepted(SpeakerVoiceGender.Male, 0.95),
            [308] = Accepted(SpeakerVoiceGender.Male, 0.87),
            [309] = Accepted(SpeakerVoiceGender.Female, 0.93)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, genders, 307);

        Assert.True(result.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Female, result.TargetGender);
        Assert.Equal("reply_after_speaker_continuation", result.ReasonCode);
        Assert.Equal("SPEAKER_05", result.TargetSpeakerId);
    }

    [Fact]
    public void ContinuationBeyondTwoCues_IsNotFollowed()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "Did you?"), Cue(2, 1.2, 2, "Did you?"), Cue(3, 2.2, 3, "Did you?"),
            Cue(4, 3.2, 4, "Did you?"), Cue(5, 4.2, 5, "No.")
        };
        var speakers = new Dictionary<int, string?> { [1] = "A", [2] = "A", [3] = "A", [4] = "A", [5] = "B" };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.95),
            [5] = Accepted(SpeakerVoiceGender.Female, 0.95)
        };

        var result = HardVoiceTurnResolver.Resolve(cues, speakers, genders, 1);

        Assert.False(result.IsResolved);
        Assert.Equal("same_speaker_turn", result.ReasonCode);
    }

    [Fact]
    public void ContinuationFollowedByALongPause_IsNotFollowed()
    {
        var cues = new[] { Cue(1, 0, 1, "Did you?"), Cue(2, 1.2, 2, "Really?"), Cue(3, 9, 10, "No.") };
        var speakers = new Dictionary<int, string?> { [1] = "A", [2] = "A", [3] = "B" };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.95),
            [3] = Accepted(SpeakerVoiceGender.Female, 0.95)
        };

        Assert.False(HardVoiceTurnResolver.Resolve(cues, speakers, genders, 1).IsResolved);
    }

    [Fact]
    public void ContinuationWhosePitchDisagreesWithTheSpeaker_StopsTheSearch()
    {
        // Same label, but the middle cue sounds like someone else: the labels are
        // not trustworthy here, so no addressee is inferred.
        var cues = new[] { Cue(1, 0, 1, "Did you?"), Cue(2, 1.2, 2, "What?"), Cue(3, 2.2, 3, "No.") };
        var speakers = new Dictionary<int, string?> { [1] = "A", [2] = "A", [3] = "B" };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.95),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.84),
            [3] = Accepted(SpeakerVoiceGender.Female, 0.95)
        };

        Assert.False(HardVoiceTurnResolver.Resolve(cues, speakers, genders, 1).IsResolved);
    }

    [Fact]
    public void Review_QuestionSpreadOverTwoCues_RewritesBothToTheAnsweringWoman()
    {
        var source = new[]
        {
            Cue(307, 0, 2, "Did you or did you not make love to him?"),
            Cue(308, 2.2, 4, "Were you not lovers with Bill Hayden?"),
            Cue(309, 4.3, 6, "Are you asking if me and Bill Hayden fucked?")
        };
        var translated = new[]
        {
            Cue(307, 0, 2, "Kochałeś się z nim, czy nie?"),
            Cue(308, 2.2, 4, "Nie byłeś kochankiem Billa Haydena?"),
            Cue(309, 4.3, 6, "Pytasz, czy ja i Bill Hayden się pieprzyliśmy?")
        };
        var speakers = new Dictionary<int, string?> { [307] = "SPEAKER_02", [308] = "SPEAKER_02", [309] = "SPEAKER_05" };
        var genders = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [307] = Accepted(SpeakerVoiceGender.Male, 0.95),
            [308] = Accepted(SpeakerVoiceGender.Male, 0.92),
            [309] = Accepted(SpeakerVoiceGender.Female, 0.93)
        };

        var result = new DeterministicGenderReviewService().Review(
            source, translated, speakers, new Dictionary<string, SpeakerGenderEvidence>(), genders,
            hardVoiceTurnOnly: true);

        Assert.Equal("Kochałaś się z nim, czy nie?", result[0].Text);
        Assert.Equal("Nie byłaś kochankiem Billa Haydena?", result[1].Text);
    }

    [Fact]
    public void Review_PastCopulaPredicateFollowsTheCorrectedVerb()
    {
        // S01E08 #431 came out as "byłem kuszona".
        var source = new[] { Cue(431, 0, 2, "I'll admit, in the moment, I was definitely tempted."), Cue(432, 2.2, 4, "Now that I see...") };
        var translated = new[] { Cue(431, 0, 2, "Przyznam, że w tej chwili zdecydowanie byłam kuszona."), Cue(432, 2.2, 4, "Teraz, gdy widzę...") };
        var speakers = new Dictionary<int, string?> { [431] = "SPEAKER_01", [432] = "SPEAKER_01" };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Male, 0.86, 5)
        };

        var result = new DeterministicGenderReviewService().Review(
            source, translated, speakers, speakerGender, new Dictionary<int, CueVoiceGenderEvidence>(),
            hardVoiceTurnOnly: true);

        Assert.Equal("Przyznam, że w tej chwili zdecydowanie byłem kuszony.", result[0].Text);
    }
}
