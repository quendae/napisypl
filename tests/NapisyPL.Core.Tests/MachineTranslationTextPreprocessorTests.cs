using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class MachineTranslationTextPreprocessorTests
{
    [Fact]
    public void Prepare_KeepsMultipleSentencesInOneCue()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(253, "Do you think they're ever gonna\nforget today? Never.")
        ]);

        Assert.Equal(
            ["Do you think they're ever gonna\nforget today? Never."],
            batch.Parts);
        Assert.Equal([1], batch.PartCounts);
        Assert.Equal([253], batch.OriginalSegments.Select(segment => segment.Id));
    }

    [Fact]
    public void Prepare_PreservesTwoLineDialogueInOneCue()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(
                30,
                "<i>- Yeah, sí, problema.</i>\n<i>- And now dos problemas.</i>")
        ]);

        Assert.Equal(
            ["<i>- Yeah, sí, problema.</i>\n<i>- And now dos problemas.</i>"],
            batch.Parts);
        Assert.Equal([1], batch.PartCounts);
    }

    [Fact]
    public void Prepare_LeavesEmptyCueOutOfModelInputs()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(1, "   "),
            new TranslationSegment(2, "One. Two.")
        ]);

        Assert.Equal(["One. Two."], batch.Parts);
        Assert.Equal([0, 1], batch.PartCounts);

        var result = sut.Reassemble(batch, ["Jeden. Dwa."]);
        Assert.Equal(string.Empty, result[1]);
        Assert.Equal("Jeden. Dwa.", result[2]);
    }

    [Fact]
    public void Reassemble_MapsTranslatedPartsBackToOriginalCue()
    {
        var sut = new MachineTranslationTextPreprocessor();
        var batch = sut.Prepare([
            new TranslationSegment(18, "She blamed me for it. That is serious.")
        ]);

        var result = sut.Reassemble(batch, ["Obwiniła mnie za to. To poważna sprawa."]);

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
