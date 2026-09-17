using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class FinalTranslationQualityGateTests
{
    [Fact]
    public void Evaluate_AcceptsNormalTranslationAndShortLegitimateRepetition()
    {
        var source = Cues("No, no, no. I told you already.");
        var translated = Cues("Nie, nie, nie. Już ci mówiłem.");

        var issues = FinalTranslationQualityGate.Evaluate(source, translated);

        Assert.Empty(issues);
    }

    [Fact]
    public void Evaluate_FlagsLargeSymbolRun()
    {
        var issues = FinalTranslationQualityGate.Evaluate(
            Cues("Who gives up?"),
            Cues("Kto się poddaje? " + new string('*', 180)));

        var issue = Assert.Single(issues);
        Assert.Equal("symbol_run", issue.Reason);
    }

    [Fact]
    public void Evaluate_FlagsDominantRepeatedWord()
    {
        var issues = FinalTranslationQualityGate.Evaluate(
            Cues("No."),
            Cues(string.Join(", ", Enumerable.Repeat("Nie", 80))));

        var issue = Assert.Single(issues);
        Assert.Equal("dominant_token", issue.Reason);
    }

    [Fact]
    public void Evaluate_FlagsRepeatedPhraseLoop()
    {
        var loop = string.Join(" ", Enumerable.Repeat("tak właśnie jest", 20));
        var issues = FinalTranslationQualityGate.Evaluate(
            Cues("That is how it is."),
            Cues(loop));

        var issue = Assert.Single(issues);
        Assert.Equal("repeated_ngram", issue.Reason);
    }

    [Fact]
    public void Evaluate_FlagsGeneratedNumericRun()
    {
        var numbers = string.Join(", ", Enumerable.Range(20, 66));
        var issues = FinalTranslationQualityGate.Evaluate(
            Cues("Twenty, um..."),
            Cues(numbers));

        var issue = Assert.Single(issues);
        Assert.Equal("numeric_run", issue.Reason);
    }

    [Fact]
    public void Evaluate_FlagsLengthExplosionEvenWithoutOneDominantToken()
    {
        var output = string.Join(" ", Enumerable.Range(1, 110).Select(index => $"słowo{index}"));
        var issues = FinalTranslationQualityGate.Evaluate(
            Cues("Okay."),
            Cues(output));

        var issue = Assert.Single(issues);
        Assert.Equal("length_explosion", issue.Reason);
    }

    [Fact]
    public void ReplaceIssuesWithSource_PreservesGoodTranslationsAndFallsBackOnlyForBadCue()
    {
        var source = new[]
        {
            new SubtitleCue(325, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Good morning."),
            new SubtitleCue(326, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "This one failed."),
            new SubtitleCue(327, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(6), "See you later.")
        };
        var translated = new[]
        {
            source[0] with { Text = "Dzień dobry." },
            source[1] with { Text = string.Join(" ", Enumerable.Repeat("pętla modelu", 20)) },
            source[2] with { Text = "Do zobaczenia później." }
        };
        var issue = Assert.Single(FinalTranslationQualityGate.Evaluate(source, translated));

        var recovered = FinalTranslationQualityGate.ReplaceIssuesWithSource(
            source,
            translated,
            [issue]);

        Assert.Equal("Dzień dobry.", recovered[0].Text);
        Assert.Equal("This one failed.", recovered[1].Text);
        Assert.Equal("Do zobaczenia później.", recovered[2].Text);
        Assert.Equal(source[1].Start, recovered[1].Start);
        Assert.Equal(source[1].End, recovered[1].End);
    }

    private static IReadOnlyList<SubtitleCue> Cues(string text) =>
        [new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(2), text)];
}
