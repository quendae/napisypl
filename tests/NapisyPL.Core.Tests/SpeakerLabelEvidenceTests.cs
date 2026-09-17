using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>Cases from the Chance S01 run, where SDH labels named speakers audio got wrong.</summary>
public class SpeakerLabelEvidenceTests
{
    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static CueVoiceGenderEvidence Accepted(SpeakerVoiceGender gender, double confidence) =>
        new(gender, confidence, 0.30, 1.5, gender, confidence);

    [Theory]
    [InlineData("Jaclyn: I saw it at a thrift\nstore.", "Jaclyn")]
    [InlineData("JACLYN: Outside my window", "JACLYN")]
    [InlineData("- Woman over PA: Attention.", "Woman over PA")]
    [InlineData("[ Sighs ] Chance: Hey.", "Chance")]
    [InlineData("NICOLE (on phone): Dad?", "NICOLE")]
    public void SingleSpeakerLabelsAreRead(string text, string expected)
    {
        Assert.True(SpeakerLabelEvidence.TryGetSingleSpeakerLabel(text, out var label));
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData("- Chance: Hey.\n- Jaclyn: Hi.")]
    [InlineData("It's 10:30 already.")]
    [InlineData("Note: this is not a speaker")]
    [InlineData("I told Mr. D --\nIt's okay, Lucy.")]
    public void NonSpeakerTextOrTwoSpeakersGiveNoLabel(string text) =>
        Assert.False(SpeakerLabelEvidence.TryGetSingleSpeakerLabel(text, out _));

    [Theory]
    [InlineData("Jaclyn", SpeakerVoiceGender.Female)]
    [InlineData("CHANCE", SpeakerVoiceGender.Male)]
    [InlineData("Woman over PA", SpeakerVoiceGender.Female)]
    [InlineData("Young man", SpeakerVoiceGender.Male)]
    [InlineData("Mrs. Hudson", SpeakerVoiceGender.Female)]
    [InlineData("Blackstone", SpeakerVoiceGender.Unknown)]
    public void LabelGenderComesFromRolesAndFirstNames(string label, SpeakerVoiceGender expected) =>
        Assert.Equal(expected, SpeakerLabelEvidence.GenderOfLabel(label));

    [Fact]
    public void SurnameLabelTakesTheGenderItsVoiceAgreesOn()
    {
        var source = new[]
        {
            Cue(1, 0, 2, "Blackstone: Get in."),
            Cue(2, 10, 12, "Blackstone: Now."),
            Cue(3, 20, 22, "Blackstone: Move.")
        };
        var cues = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.93),
            [2] = Accepted(SpeakerVoiceGender.Male, 0.91)
        };

        var analysis = SpeakerLabelEvidence.Analyze(source, cues);

        Assert.All(new[] { 1, 2, 3 }, id => Assert.Equal(SpeakerVoiceGender.Male, analysis.CueGender[id]));
    }

    [Fact]
    public void LabelledVoiceOverRewritesTheSelfFormAgainstAMaleProfile()
    {
        // S01E06 #665: Jaclyn's voice-over, clustered with and pitched like a man.
        var source = new[] { Cue(665, 100, 103, "Jaclyn: I saw it at a thrift\nstore. It reminded me of you.") };
        var translated = new[] { Cue(665, 100, 103, "Jaclyn: Widziałem ją w sklepie z używanymi rzeczami. Przypomniało mi o tobie.") };
        var speakers = new Dictionary<int, string?> { [665] = "SPEAKER_01" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["SPEAKER_01"] = new(SpeakerVoiceGender.Male, 0.878, 8) };
        var cues = new Dictionary<int, CueVoiceGenderEvidence> { [665] = Accepted(SpeakerVoiceGender.Male, 0.83) };

        var labels = SpeakerLabelEvidence.Analyze(source, cues);
        var (labeledCues, labeledProfiles) = SpeakerLabelEvidence.Apply(labels, source, speakers, profiles, cues);
        var reviewed = new DeterministicGenderReviewService().Review(
            source, translated, speakers, labeledProfiles, labeledCues, hardVoiceTurnOnly: true, labeledCueGender: labels.CueGender);

        Assert.StartsWith("Jaclyn: Widziałam ją", reviewed[0].Text);
    }

    [Fact]
    public void ProfileThatLabelsShowToBeTwoPeopleIsDropped()
    {
        var source = new[]
        {
            Cue(9, 0, 2, "Nicole: They drove me someplace"),
            Cue(20, 30, 32, "Jaclyn: Outside my window"),
            Cue(30, 60, 62, "Chance: Hey.")
        };
        var speakers = new Dictionary<int, string?> { [9] = "S1", [20] = "S1", [30] = "S1" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["S1"] = new(SpeakerVoiceGender.Male, 0.878, 8) };

        var labels = SpeakerLabelEvidence.Analyze(source, null);
        var (_, labeledProfiles) = SpeakerLabelEvidence.Apply(labels, source, speakers, profiles, new Dictionary<int, CueVoiceGenderEvidence>());

        Assert.Equal(SpeakerVoiceGender.Unknown, labeledProfiles["S1"].Gender);
    }

    [Theory]
    [InlineData("Zrobiłabym to wszystko sama.", SpeakerVoiceGender.Male, "Zrobiłbym to wszystko sam.")]
    [InlineData("Zrobiłbym to sam.", SpeakerVoiceGender.Female, "Zrobiłabym to sama.")]
    [InlineData("Zrobiłabym to samo co ty.", SpeakerVoiceGender.Male, "Zrobiłbym to samo co ty.")]
    [InlineData("Zrobiłabym to w tej samej chwili, ta sama osoba.", SpeakerVoiceGender.Male, "Zrobiłbym to w tej samej chwili, ta sama osoba.")]
    public void AloneFollowsTheSpeaker(string polish, SpeakerVoiceGender gender, string expected)
    {
        var source = new[] { Cue(1, 0, 2, "I would've done the whole thing alone.") };
        var translated = new[] { Cue(1, 0, 2, polish) };
        var speakers = new Dictionary<int, string?> { [1] = "A" };
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["A"] = new(gender, 0.95, 8) };

        var reviewed = new DeterministicGenderReviewService().Review(
            source, translated, speakers, profiles, new Dictionary<int, CueVoiceGenderEvidence>(), hardVoiceTurnOnly: true);

        Assert.Equal(expected, reviewed[0].Text);
    }
}
