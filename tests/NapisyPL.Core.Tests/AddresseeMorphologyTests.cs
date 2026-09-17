using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// The "you -> chciałaś" direction: correcting second-person forms to the
/// resolved addressee's gender.
/// </summary>
public class AddresseeMorphologyTests
{
    private static SubtitleCue Cue(int index, double start, double end, string text) =>
        new(index, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static CueVoiceGenderEvidence Accepted(SpeakerVoiceGender gender, double confidence) =>
        new(gender, confidence, 0.5, 3);

    /// <summary>
    /// A male cue answered by a female next speaker, which is exactly the
    /// alternation the turn resolver keys on.
    /// </summary>
    private static string ReviewMaleToFemaleTurn(string englishSource, string polishTranslation)
    {
        var source = new[] { Cue(1, 0, 1, englishSource), Cue(2, 1, 2, "Nothing.") };
        var translated = new[] { Cue(1, 0, 1, polishTranslation), Cue(2, 1, 2, "Nic.") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_M", [2] = "SPEAKER_F" };
        var speakerGender = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_F"] = new(SpeakerVoiceGender.Female, 0.98, 3)
        };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = Accepted(SpeakerVoiceGender.Male, 0.91),
            [2] = Accepted(SpeakerVoiceGender.Female, 0.88)
        };

        var result = new DeterministicGenderReviewService().Review(
            source, translated, speakers, speakerGender, cueGender,
            diagnostics: null, hardVoiceTurnOnly: true);

        return result[0].Text;
    }

    [Theory]
    [InlineData("What did you do?", "Co zrobiłeś?", "Co zrobiłaś?")]
    [InlineData("What did you say?", "Co powiedziałeś?", "Co powiedziałaś?")]
    [InlineData("Did you have to?", "Musiałeś?", "Musiałaś?")]
    [InlineData("Where did you go back to?", "Gdzie wróciłeś?", "Gdzie wróciłaś?")]
    [InlineData("Did you forget?", "Zapomniałeś?", "Zapomniałaś?")]
    public void SecondPersonPastVerb_TakesTheAddresseeGender(
        string english, string polish, string expected)
    {
        Assert.Equal(expected, ReviewMaleToFemaleTurn(english, polish));
    }

    [Theory]
    [InlineData("Are you ready?", "Jesteś gotowy?", "Jesteś gotowa?")]
    [InlineData("Are you sure?", "Jesteś pewien?", "Jesteś pewna?")]
    [InlineData("Are you tired?", "Jesteś zmęczony?", "Jesteś zmęczona?")]
    public void SecondPersonPredicateAdjective_TakesTheAddresseeGender(
        string english, string polish, string expected)
    {
        Assert.Equal(expected, ReviewMaleToFemaleTurn(english, polish));
    }

    [Fact]
    public void WithoutASecondPersonPronounInTheSource_NoAddresseeRewriteIsAuthorised()
    {
        // "He left" carries no addressee, so a Polish second-person ending here is
        // the translator's artefact, not something to agree with a speaker's gender.
        Assert.Equal("Wyszedłeś", ReviewMaleToFemaleTurn("He left.", "Wyszedłeś"));
    }

    [Fact]
    public void UnknownVerbStem_IsLeftAloneRatherThanGuessed()
    {
        // "kisiel" style words ending in -łeś must not be invented into -łaś.
        Assert.Equal("Czy to twój wesołeś?", ReviewMaleToFemaleTurn("Is that your thing?", "Czy to twój wesołeś?"));
    }

    [Fact]
    public void IrregularAddresseeVerb_UsesTheLexiconNotTheSuffixRule()
    {
        Assert.Equal("Gdzie poszłaś?", ReviewMaleToFemaleTurn("Where did you go?", "Gdzie poszedłeś?"));
    }

    [Fact]
    public void FirstPersonFormsAreNotTouchedByTheAddresseeDirection()
    {
        // The speaker is male here; his own "-łem" must survive a female addressee.
        Assert.Equal("Powiedziałem ci", ReviewMaleToFemaleTurn("I told you.", "Powiedziałem ci"));
    }

    [Theory]
    // None of these stems were on the old hand-kept list.
    [InlineData("Did you explain it?", "Wyjaśniłeś to?", "Wyjaśniłaś to?")]
    [InlineData("Did you sleep?", "Przespałeś się?", "Przespałaś się?")]
    [InlineData("Would you understand?", "Zrozumiałbyś?", "Zrozumiałabyś?")]
    [InlineData("Could you do it?", "Mógłbyś to zrobić?", "Mogłabyś to zrobić?")]
    [InlineData("Where did you come from?", "Skąd przyszedłeś?", "Skąd przyszłaś?")]
    public void LexiconCoversVerbsBeyondTheOldStemList(string english, string polish, string expected)
    {
        Assert.Equal(expected, ReviewMaleToFemaleTurn(english, polish));
    }

    [Theory]
    [InlineData("Are you hungry?", "Jesteś głodny?", "Jesteś głodna?")]
    [InlineData("Are you injured?", "Jesteś ranny?", "Jesteś ranna?")]
    [InlineData("Are you nervous?", "Jesteś zdenerwowany?", "Jesteś zdenerwowana?")]
    public void LexiconCoversPredicatesBeyondTheOldList(string english, string polish, string expected)
    {
        Assert.Equal(expected, ReviewMaleToFemaleTurn(english, polish));
    }
}

public class GenderFormLexiconTests
{
    [Fact]
    public void EmbeddedLexiconLoadsWithAllThreeCategories()
    {
        var lexicon = GenderFormLexicon.Default;

        Assert.True(lexicon.SelfMaleToFemale.Count > 50_000);
        Assert.True(lexicon.AddresseeMaleToFemale.Count > 50_000);
        Assert.True(lexicon.PredicateMaleToFemale.Count > 50_000);
    }

    [Theory]
    [InlineData("wyjaśniłem", "wyjaśniłam")]
    [InlineData("mógłbym", "mogłabym")]
    [InlineData("poszedłem", "poszłam")]
    public void SelfPairsAreReversible(string masculine, string feminine)
    {
        var lexicon = GenderFormLexicon.Default;

        Assert.Equal(feminine, lexicon.SelfMaleToFemale[masculine]);
        Assert.Equal(masculine, lexicon.SelfFemaleToMale[feminine]);
    }

    [Theory]
    [InlineData("wysyłam")] // present tense, not a feminine past form
    [InlineData("gram")]    // noun and present tense
    public void HomographsAreNotRewriteTargets(string form)
    {
        var lexicon = GenderFormLexicon.Default;

        Assert.False(lexicon.SelfFemaleToMale.ContainsKey(form));
        Assert.False(lexicon.SelfMaleToFemale.ContainsKey(form));
    }

    [Fact]
    public void ParseIgnoresCommentsAndUnknownCategories()
    {
        var lexicon = GenderFormLexicon.Parse(new StringReader(string.Join(
            Environment.NewLine,
            "#sgjp test",
            string.Join('\t', "self", "byłem", "byłam"),
            string.Join('\t', "bogus", "x", "y"))));

        Assert.Equal("byłam", lexicon.SelfMaleToFemale["byłem"]);
        Assert.Single(lexicon.SelfMaleToFemale);
    }
}
