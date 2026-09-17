using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// Cases taken from the The Leftovers S01E01 end-to-end run.
/// </summary>
public class LeftoversSelfGenderTests
{
    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static CueVoiceGenderEvidence Accepted(SpeakerVoiceGender gender, double confidence) =>
        new(gender, confidence, 0.30, 1.5, gender, confidence);

    private static IReadOnlyList<SubtitleCue> Review(
        SubtitleCue[] source,
        SubtitleCue[] translated,
        Dictionary<int, string?> speakers,
        Dictionary<string, SpeakerGenderEvidence> profiles,
        Dictionary<int, CueVoiceGenderEvidence> cues) =>
        new DeterministicGenderReviewService().Review(
            source, translated, speakers, profiles, cues, hardVoiceTurnOnly: true);

    [Fact]
    public void ProfileContradictedByTheCueAndItsContinuation_DoesNotRewriteSelfForm()
    {
        // #635 "I was in a parking lot..." leans male, #636 "...at the laundromat."
        // is confidently male, yet the cluster profile is female.
        var source = new[]
        {
            Cue(635, 3547.252, 3550.087, "I was in a parking lot..."),
            Cue(636, 3552.507, 3553.549, "...at the laundromat.")
        };
        var translated = new[]
        {
            Cue(635, 3547.252, 3550.087, "Byłem na parkingu..."),
            Cue(636, 3552.507, 3553.549, "...w pralni.")
        };
        var speakers = new Dictionary<int, string?> { [635] = "SPEAKER_00", [636] = "SPEAKER_00" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Female, 0.907, 8)
        };
        var cues = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [635] = new(SpeakerVoiceGender.Unknown, 0, 0.02, 2.8, SpeakerVoiceGender.Male, 0.815),
            [636] = Accepted(SpeakerVoiceGender.Male, 0.888)
        };

        Assert.Equal("Byłem na parkingu...", Review(source, translated, speakers, profiles, cues)[0].Text);
    }

    [Fact]
    public void ProfileContradictedByConfidentCuePitch_DoesNotRewriteSelfForm()
    {
        var source = new[] { Cue(1, 0, 2, "I was in a parking lot.") };
        var translated = new[] { Cue(1, 0, 2, "Byłem na parkingu.") };
        var speakers = new Dictionary<int, string?> { [1] = "A" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["A"] = new(SpeakerVoiceGender.Female, 0.95, 8) };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [1] = Accepted(SpeakerVoiceGender.Male, 0.86) };

        Assert.Equal("Byłem na parkingu.", Review(source, translated, speakers, profiles, cues)[0].Text);
    }

    [Theory]
    [InlineData(284, "I didn't even know you're capable of saying the word cunt.",
        "Nawet nie wiedziałem, że jesteś w stanie powiedzieć słowo cipko.",
        "Nawet nie wiedziałam, że jesteś w stanie powiedzieć słowo cipko.")]
    [InlineData(666, "I was wondering if I could stay here.",
        "Zastanawiałem się, czy mogę tu zostać.",
        "Zastanawiałam się, czy mogę tu zostać.")]
    public void WeakProfileConfirmedByConfidentCuePitch_RewritesSelfForm(
        int id, string english, string polish, string expected)
    {
        var source = new[] { Cue(id, 0, 3, english), Cue(id + 1, 3.2, 4.5, "...") };
        var translated = new[] { Cue(id, 0, 3, polish), Cue(id + 1, 3.2, 4.5, "...") };
        var speakers = new Dictionary<int, string?> { [id] = "SPEAKER_03", [id + 1] = "SPEAKER_02" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_03"] = new(SpeakerVoiceGender.Female, 0.825, 6)
        };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [id] = Accepted(SpeakerVoiceGender.Female, 0.921) };

        Assert.Equal(expected, Review(source, translated, speakers, profiles, cues)[0].Text);
    }

    [Fact]
    public void WeakProfileWithOnlyModerateCuePitch_IsNotEnough()
    {
        var source = new[] { Cue(666, 0, 3, "I was wondering if I could stay here.") };
        var translated = new[] { Cue(666, 0, 3, "Zastanawiałem się, czy mogę tu zostać.") };
        var speakers = new Dictionary<int, string?> { [666] = "SPEAKER_03" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_03"] = new(SpeakerVoiceGender.Female, 0.825, 6)
        };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [666] = Accepted(SpeakerVoiceGender.Female, 0.86) };

        Assert.Equal("Zastanawiałem się, czy mogę tu zostać.", Review(source, translated, speakers, profiles, cues)[0].Text);
    }

    [Fact]
    public void ConfidentCuePitchWithoutAnyProfile_IsNotEnough()
    {
        var source = new[] { Cue(666, 0, 3, "I was wondering if I could stay here.") };
        var translated = new[] { Cue(666, 0, 3, "Zastanawiałem się, czy mogę tu zostać.") };
        var speakers = new Dictionary<int, string?> { [666] = "SPEAKER_03" };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [666] = Accepted(SpeakerVoiceGender.Female, 0.97) };

        Assert.Equal(
            "Zastanawiałem się, czy mogę tu zostać.",
            Review(source, translated, speakers, new Dictionary<string, SpeakerGenderEvidence>(), cues)[0].Text);
    }

    [Theory]
    [InlineData("Powiedziałabym coś wcześniej, ale byłam tak zaniepokojona.",
        "Powiedziałbym coś wcześniej, ale byłem tak zaniepokojony.")]
    [InlineData("Powiedziałbym coś wcześniej, ale byłem tak zaniepokojona.",
        "Powiedziałbym coś wcześniej, ale byłem tak zaniepokojony.")]
    public void PredicateAfterAnAdverb_FollowsTheSpeaker(string polish, string expected)
    {
        // #217 "I would've said something sooner, but I was so riveted."
        var source = new[] { Cue(217, 0, 2.5, "I would've said something sooner, but I was so riveted.") };
        var translated = new[] { Cue(217, 0, 2.5, polish) };
        var speakers = new Dictionary<int, string?> { [217] = "SPEAKER_10" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_10"] = new(SpeakerVoiceGender.Male, 0.904, 6)
        };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [217] = Accepted(SpeakerVoiceGender.Male, 0.94) };

        Assert.Equal(expected, Review(source, translated, speakers, profiles, cues)[0].Text);
    }

    [Theory]
    [InlineData(SpeakerVoiceGender.Female, "Jestem już gotowy.", "Jestem już gotowa.")]
    [InlineData(SpeakerVoiceGender.Female, "Jestem bardzo zmęczony.", "Jestem bardzo zmęczona.")]
    public void FirstPersonPredicate_SkipsUpToTwoAdverbs(SpeakerVoiceGender gender, string polish, string expected)
    {
        var source = new[] { Cue(1, 0, 2, "I'm ready.") };
        var translated = new[] { Cue(1, 0, 2, polish) };
        var speakers = new Dictionary<int, string?> { [1] = "A" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["A"] = new(gender, 0.95, 5) };

        Assert.Equal(expected, Review(source, translated, speakers, profiles, new Dictionary<int, CueVoiceGenderEvidence>())[0].Text);
    }
}
