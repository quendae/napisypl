using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class MachineTranslationTextPreprocessorTests
{
    [Fact]
    public void Prepare_JoinsSoftWrapBeforeSentenceSplit()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(253, "Do you think they're ever gonna\nforget today? Never.")
        ]);

        Assert.Equal(
            ["Do you think they're ever gonna forget today?", "Never."],
            batch.Parts);
        Assert.Equal([2], batch.PartCounts);
        Assert.Equal([253], batch.OriginalSegments.Select(segment => segment.Id));
    }

    [Fact]
    public void Prepare_SplitsTaggedDialogueTurns()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(
                30,
                "<i>- Yeah, sí, problema.</i>\n<i>- And now dos problemas.</i>")
        ]);

        Assert.Equal(
            ["<i>- Yeah, sí, problema.</i>", "<i>- And now dos problemas.</i>"],
            batch.Parts);
        Assert.Equal([2], batch.PartCounts);
    }

    [Theory]
    [InlineData("Mr. Goodman is here. Really?", "Mr. Goodman is here.", "Really?")]
    [InlineData("Dr. Caldera called. Answer him.", "Dr. Caldera called.", "Answer him.")]
    [InlineData("Use it, e.g. today. Fine.", "Use it, e.g. today.", "Fine.")]
    public void Prepare_PreservesDotAbbreviationHandling(
        string input,
        string firstPart,
        string secondPart)
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([new TranslationSegment(1, input)]);

        Assert.Equal([firstPart, secondPart], batch.Parts);
    }

    [Fact]
    public void Reassemble_MapsTranslatedPartsBackToOriginalCue()
    {
        var sut = new MachineTranslationTextPreprocessor();
        var batch = sut.Prepare([
            new TranslationSegment(18, "She blamed me for it. That is serious.")
        ]);

        var result = sut.Reassemble(batch, ["Obwiniła mnie za to.", "To poważna sprawa."]);

        Assert.Equal("Obwiniła mnie za to. To poważna sprawa.", result[18]);
    }

    [Fact]
    public void Reassemble_RejectsTranslatedPartCountMismatch()
    {
        var sut = new MachineTranslationTextPreprocessor();
        var batch = sut.Prepare([
            new TranslationSegment(1, "One."),
            new TranslationSegment(2, "Two.")
        ]);
        Action act = () => _ = sut.Reassemble(batch, new[] { "Jeden." });

        Assert.Throws<InvalidDataException>(act);
    }
}
