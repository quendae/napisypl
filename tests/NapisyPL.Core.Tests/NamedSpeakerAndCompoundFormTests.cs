using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// A diarized cluster the subtitles name over and over, and the compound forms that have to
/// follow the gender once the cluster's own lines are rewritten.
/// </summary>
public sealed class NamedSpeakerAndCompoundFormTests
{
    private static SubtitleCue Cue(int index, string text) =>
        new(index, TimeSpan.FromSeconds(index * 2), TimeSpan.FromSeconds(index * 2 + 1.5), text);

    private static (SpeakerLabelAnalysis Labels, IReadOnlyDictionary<string, SpeakerGenderEvidence> Speakers)
        AnalyzeNamed(IReadOnlyList<SubtitleCue> source, IReadOnlyDictionary<int, string?> cueSpeakers,
            IReadOnlyDictionary<string, SpeakerGenderEvidence>? profiles = null)
    {
        var labels = SpeakerLabelEvidence.Analyze(source, null);
        var (_, speakers) = SpeakerLabelEvidence.Apply(
            labels,
            source,
            cueSpeakers,
            profiles ?? new Dictionary<string, SpeakerGenderEvidence>(),
            new Dictionary<int, CueVoiceGenderEvidence>());
        return (labels, speakers);
    }

    [Fact]
    public void ARepeatedlyNamedClusterTakesThatNamesGender()
    {
        var source = new[]
        {
            Cue(1, "JACLYN: I was there."),
            Cue(2, "Nothing happened."),
            Cue(3, "LUCY: You have to tell him.")
        };
        var cueSpeakers = new Dictionary<int, string?> { [1] = "SPEAKER_01", [2] = "SPEAKER_01", [3] = "SPEAKER_01" };

        var (_, speakers) = AnalyzeNamed(source, cueSpeakers);

        // Two women's names on one voice: the voice is a woman, including her unnamed line.
        Assert.True(SpeakerGenderReviewEligibility.IsEligible(speakers["SPEAKER_01"]));
        Assert.Equal(SpeakerVoiceGender.Female, speakers["SPEAKER_01"].Gender);
    }

    [Fact]
    public void AClusterNamedAsBothGendersIsDropped()
    {
        var source = new[]
        {
            Cue(1, "JACLYN: I was there."),
            Cue(2, "LUCY: So was I."),
            Cue(3, "CARL: And I drove."),
            Cue(4, "RAYMOND: We all did.")
        };
        var cueSpeakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_01", [2] = "SPEAKER_01", [3] = "SPEAKER_01", [4] = "SPEAKER_01"
        };
        var profiles = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Female, 0.97, 6)
        };

        var (_, speakers) = AnalyzeNamed(source, cueSpeakers, profiles);

        Assert.Equal(SpeakerVoiceGender.Unknown, speakers["SPEAKER_01"].Gender);
    }

    [Fact]
    public void OneNamedCueIsNotEnoughToNameTheVoice()
    {
        var source = new[] { Cue(1, "JACLYN: I was there."), Cue(2, "Nothing happened.") };
        var cueSpeakers = new Dictionary<int, string?> { [1] = "SPEAKER_01", [2] = "SPEAKER_01" };

        var (_, speakers) = AnalyzeNamed(source, cueSpeakers);

        Assert.False(speakers.ContainsKey("SPEAKER_01"));
    }

    /// <summary>One cue, one speaker with a settled voice: only the morphology is under test.</summary>
    private static string ReviewSelf(string english, string polish, SpeakerVoiceGender gender)
    {
        var profiles = new Dictionary<string, SpeakerGenderEvidence> { ["SPEAKER_01"] = new(gender, 0.97, 6) };
        return new DeterministicGenderReviewService().Review(
            [Cue(1, english)],
            [Cue(1, polish)],
            new Dictionary<int, string?> { [1] = "SPEAKER_01" },
            profiles).Single().Text;
    }

    [Theory]
    // The person sits on the conjunction, the gender on a participle of its own.
    [InlineData("I should never have asked for it.", "Nigdy nie powinienem był o to prosić.",
        "Nigdy nie powinnam była o to prosić.")]
    [InlineData("If I was better at it, I'd sell more.", "Gdybym był w tym lepszy, sprzedałbym więcej.",
        "Gdybym była w tym lepsza, sprzedałabym więcej.")]
    [InlineData("If I had known, I wouldn't have asked.", "Gdybym wiedział, nie pytałbym.",
        "Gdybym wiedziała, nie pytałabym.")]
    // Comparatives are missing from the morphological lexicon, so the regular ending decides.
    [InlineData("I'm the best.", "Jestem najlepszy.", "Jestem najlepsza.")]
    public void CompoundSelfFormsFollowTheSpeaker(string english, string masculine, string feminine) =>
        Assert.Equal(feminine, ReviewSelf(english, masculine, SpeakerVoiceGender.Female));

    [Theory]
    // The name in the line settles who is addressed; the compound form follows her.
    [InlineData("If you'd been more careful, Jaclyn, nothing would have happened.",
        "Gdybyś był ostrożniejszy, Jaclyn, nic by się nie stało.",
        "Gdybyś była ostrożniejsza, Jaclyn, nic by się nie stało.")]
    [InlineData("You should have called, Jaclyn.", "Powinieneś był zadzwonić, Jaclyn.",
        "Powinnaś była zadzwonić, Jaclyn.")]
    public void CompoundAddresseeFormsFollowTheListener(string english, string masculine, string feminine) =>
        Assert.Equal(feminine, ReviewSelf(english, masculine, SpeakerVoiceGender.Male));

    [Fact]
    public void APossessivePronounIsNotAComparative() =>
        Assert.Equal("To jest nasza.", ReviewSelf("It is ours.", "To jest nasza.", SpeakerVoiceGender.Male));
}
