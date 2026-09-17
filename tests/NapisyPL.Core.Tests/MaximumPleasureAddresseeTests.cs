using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// Cases taken from the Maximum Pleasure Guaranteed S01E01 end-to-end run, where
/// most conversations are between two women.
/// </summary>
public class MaximumPleasureAddresseeTests
{
    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static CueVoiceGenderEvidence Accepted(SpeakerVoiceGender gender, double confidence) =>
        new(gender, confidence, 0.30, 1.5, gender, confidence);

    private static IReadOnlyList<SubtitleCue> Review(
        SubtitleCue[] source,
        SubtitleCue[] translated,
        Dictionary<int, string?> speakers,
        Dictionary<int, CueVoiceGenderEvidence> cues,
        Dictionary<string, SpeakerGenderEvidence>? profiles = null) =>
        new DeterministicGenderReviewService().Review(
            source, translated, speakers, profiles ?? new Dictionary<string, SpeakerGenderEvidence>(), cues,
            hardVoiceTurnOnly: true);

    // --- 1. coordinated predicates -------------------------------------------------

    [Fact]
    public void CoordinatedFirstPersonPredicates_AllFollowTheSpeaker()
    {
        // #14 "And I'll fall asleep. I'm old and broken."
        var source = new[] { Cue(14, 0, 3, "And I'll fall asleep. I'm old and broken.") };
        var translated = new[] { Cue(14, 0, 3, "I zasnę. Jestem stary i zepsuty.") };
        var speakers = new Dictionary<int, string?> { [14] = "SPEAKER_06" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["SPEAKER_06"] = new(SpeakerVoiceGender.Female, 0.95, 8) };

        Assert.Equal(
            "I zasnę. Jestem stara i zepsuta.",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>(), profiles)[0].Text);
    }

    [Fact]
    public void CoordinatedPredicates_ContinuePastAnAlreadyCorrectOne()
    {
        var source = new[] { Cue(14, 0, 3, "I'm old and broken.") };
        var translated = new[] { Cue(14, 0, 3, "Jestem stara i zepsuty.") };
        var speakers = new Dictionary<int, string?> { [14] = "A" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["A"] = new(SpeakerVoiceGender.Female, 0.95, 8) };

        Assert.Equal(
            "Jestem stara i zepsuta.",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>(), profiles)[0].Text);
    }

    [Fact]
    public void CoordinationStopsAtAnotherSubject()
    {
        var source = new[] { Cue(1, 0, 3, "I'm ready, and he's ready.") };
        var translated = new[] { Cue(1, 0, 3, "Jestem gotowy, a on gotowy.") };
        var speakers = new Dictionary<int, string?> { [1] = "A" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["A"] = new(SpeakerVoiceGender.Female, 0.95, 8) };

        Assert.Equal(
            "Jestem gotowa, a on gotowy.",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>(), profiles)[0].Text);
    }

    // --- 2. A-B-A --------------------------------------------------------------------

    private static (SubtitleCue[] Source, SubtitleCue[] Translated) OldAndBroken() =>
    (
        new[]
        {
            Cue(14, 61.0, 63.5, "And I'll fall asleep. I'm old and broken."),
            Cue(15, 64.0, 65.0, "You are not old."),
            Cue(16, 65.2, 67.0, "Well, I am broken.")
        },
        new[]
        {
            Cue(14, 61.0, 63.5, "I zasnę. Jestem stara i zepsuta."),
            Cue(15, 64.0, 65.0, "Nie jesteś stary."),
            Cue(16, 65.2, 67.0, "Cóż, jestem złamana.")
        }
    );

    [Fact]
    public void BetweenTwoLinesOfTheSameWoman_AddresseeIsFemale()
    {
        var (source, translated) = OldAndBroken();
        var speakers = new Dictionary<int, string?> { [14] = "SPEAKER_06", [15] = "SPEAKER_04", [16] = "SPEAKER_06" };
        var cues = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [14] = Accepted(SpeakerVoiceGender.Female, 0.948),
            [15] = Accepted(SpeakerVoiceGender.Female, 0.93),
            [16] = Accepted(SpeakerVoiceGender.Female, 0.958)
        };

        Assert.Equal("Nie jesteś stara.", Review(source, translated, speakers, cues)[1].Text);
    }

    [Fact]
    public void Between_SurroundingPitchDisagrees_DoesNotResolve()
    {
        var (source, translated) = OldAndBroken();
        var speakers = new Dictionary<int, string?> { [14] = "SPEAKER_06", [15] = "SPEAKER_04", [16] = "SPEAKER_06" };
        var cues = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [14] = Accepted(SpeakerVoiceGender.Female, 0.948),
            [15] = Accepted(SpeakerVoiceGender.Female, 0.93),
            [16] = Accepted(SpeakerVoiceGender.Male, 0.958)
        };

        var result = HardVoiceTurnResolver.ResolveAddresseeBetweenSameSpeaker(
            source, speakers, cues, new Dictionary<string, SpeakerGenderEvidence>(), 15);

        Assert.False(result.IsResolved);
        Assert.Equal("surrounding_speaker_gender_conflict", result.ReasonCode);
    }

    [Fact]
    public void Between_DifferentSpeakersAround_DoesNotResolve()
    {
        var (source, translated) = OldAndBroken();
        var speakers = new Dictionary<int, string?> { [14] = "SPEAKER_06", [15] = "SPEAKER_04", [16] = "SPEAKER_09" };
        var cues = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [14] = Accepted(SpeakerVoiceGender.Female, 0.948),
            [16] = Accepted(SpeakerVoiceGender.Female, 0.958)
        };

        Assert.Equal("Nie jesteś stary.", Review(source, translated, speakers, cues)[1].Text);
    }

    [Fact]
    public void Between_OnlyOneSurroundingCueMeasured_NeedsAnEligibleProfile()
    {
        var (source, _) = OldAndBroken();
        var speakers = new Dictionary<int, string?> { [14] = "SPEAKER_06", [15] = "SPEAKER_04", [16] = "SPEAKER_06" };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [14] = Accepted(SpeakerVoiceGender.Female, 0.948) };

        Assert.False(HardVoiceTurnResolver.ResolveAddresseeBetweenSameSpeaker(
            source, speakers, cues, new Dictionary<string, SpeakerGenderEvidence>(), 15).IsResolved);

        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["SPEAKER_06"] = new(SpeakerVoiceGender.Female, 0.90, 8) };
        var withProfile = HardVoiceTurnResolver.ResolveAddresseeBetweenSameSpeaker(source, speakers, cues, profiles, 15);
        Assert.True(withProfile.IsResolved);
        Assert.Equal(SpeakerVoiceGender.Female, withProfile.TargetGender);
    }

    // --- 3. same-speaker run ------------------------------------------------------------

    private static (SubtitleCue[] Source, SubtitleCue[] Translated) Portland() =>
    (
        new[]
        {
            Cue(612, 1975.0, 1978.5, "Why would you tell him\nabout Portland, Paula?"),
            Cue(613, 1980.0, 1983.0, "Why would you tell fucking anyone?")
        },
        new[]
        {
            Cue(612, 1975.0, 1978.5, "Dlaczego powiedziałaś mu o Portland, Paula?"),
            Cue(613, 1980.0, 1983.0, "Dlaczego miałbyś komuś powiedzieć?")
        }
    );

    [Fact]
    public void SameSpeakerRun_CarriesTheAddresseeNamedInTheLineBefore()
    {
        var (source, translated) = Portland();
        var speakers = new Dictionary<int, string?> { [612] = "SPEAKER_14", [613] = "SPEAKER_14" };

        Assert.Equal(
            "Dlaczego miałabyś komuś powiedzieć?",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>())[1].Text);
    }

    [Fact]
    public void SameSpeakerRun_WithoutAVocativeName_IsNotAnAnchor()
    {
        var (source, translated) = Portland();
        source[0] = source[0] with { Text = "Why would you tell him about Portland?" };
        translated[0] = translated[0] with { Text = "Dlaczego powiedziałaś mu o Portland?" };
        var speakers = new Dictionary<int, string?> { [612] = "SPEAKER_14", [613] = "SPEAKER_14" };

        Assert.Equal(
            "Dlaczego miałbyś komuś powiedzieć?",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>())[1].Text);
    }

    [Fact]
    public void SameSpeakerRun_BrokenByAnotherSpeaker_IsNotFollowed()
    {
        var (source, translated) = Portland();
        var speakers = new Dictionary<int, string?> { [612] = "SPEAKER_14", [613] = "SPEAKER_08" };

        Assert.Equal(
            "Dlaczego miałbyś komuś powiedzieć?",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>())[1].Text);
    }

    [Fact]
    public void SameSpeakerRun_MasculineMtFormIsNotAnAnchor()
    {
        var source = new[]
        {
            Cue(1, 0, 2, "Why would you tell him, Paula?"),
            Cue(2, 2.5, 4, "Why would you tell anyone?")
        };
        var translated = new[]
        {
            Cue(1, 0, 2, "Dlaczego powiedziałeś mu, Paula?"),
            Cue(2, 2.5, 4, "Dlaczego powiedziałaś komuś?")
        };
        var speakers = new Dictionary<int, string?> { [1] = "A", [2] = "A" };

        Assert.Equal(
            "Dlaczego powiedziałaś komuś?",
            Review(source, translated, speakers, new Dictionary<int, CueVoiceGenderEvidence>())[1].Text);
    }
}
