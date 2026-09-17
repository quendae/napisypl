using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class MachineTranslationTextPreprocessorTests
{
    [Fact]
    public void Prepare_SplitsSentencesSoTheModelCannotDropOne()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(24, "I have real responsibilities.\nYou know that, Christine.")
        ]);

        Assert.Equal(["I have real responsibilities.", "You know that, Christine."], batch.Parts);
        Assert.Equal([2], batch.PartCounts);
    }

    [Fact]
    public void Prepare_NeverSendsALineBreakToTheModel()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(246, "We don't want\nto live here no more.")
        ]);

        Assert.Equal(["We don't want to live here no more."], batch.Parts);
        Assert.DoesNotContain(batch.Parts, part => part.Contains('\n'));
    }

    [Fact]
    public void Prepare_DoesNotSplitAfterATitleAbbreviation()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([new TranslationSegment(11, "Oh, Mr. Russell... Hello.")]);

        Assert.Equal(["Oh, Mr. Russell...", "Hello."], batch.Parts);
    }

    [Fact]
    public void Prepare_QuestionThenAnswerBecomesTwoParts()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(253, "Do you think they're ever gonna\nforget today? Never.")
        ]);

        Assert.Equal(["Do you think they're ever gonna forget today?", "Never."], batch.Parts);
    }

    [Fact]
    public void TwoLineDialogue_KeepsOneOutputLinePerSpeaker()
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

        var result = sut.Reassemble(batch, ["<i>- Tak, problem.</i>", "<i>- A teraz dwa problemy.</i>"]);

        Assert.Equal("<i>- Tak, problem.</i>\n<i>- A teraz dwa problemy.</i>", result[30]);
    }

    [Fact]
    public void DialogueLineContinuation_StaysWithItsSpeaker()
    {
        var sut = new MachineTranslationTextPreprocessor();
        var batch = sut.Prepare([
            new TranslationSegment(5, "- Where are you\ngoing?\n- Home.")
        ]);

        Assert.Equal(["- Where are you going?", "- Home."], batch.Parts);
    }

    [Fact]
    public void Prepare_LeavesEmptyCueOutOfModelInputs()
    {
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(1, "   "),
            new TranslationSegment(2, "One. Two.")
        ]);

        Assert.Equal(["One.", "Two."], batch.Parts);
        Assert.Equal([0, 2], batch.PartCounts);

        var result = sut.Reassemble(batch, ["Jeden.", "Dwa."]);
        Assert.Equal(string.Empty, result[1]);
        Assert.Equal("Jeden. Dwa.", result[2]);
    }

    [Fact]
    public void Reassemble_MapsTranslatedPartsBackToOriginalCue()
    {
        var sut = new MachineTranslationTextPreprocessor();
        var batch = sut.Prepare([
            new TranslationSegment(18, "She blamed me for it. That is serious."),
            new TranslationSegment(19, "Okay.")
        ]);

        var result = sut.Reassemble(batch, ["Obwiniła mnie za to.", "To poważna sprawa.", "Dobrze."]);

        Assert.Equal("Obwiniła mnie za to. To poważna sprawa.", result[18]);
        Assert.Equal("Dobrze.", result[19]);
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

    [Fact]
    public void Prepare_DashOnlyOnTheSecondLine_IsTwoSpeakers()
    {
        // Doc S02E19 #191: joined into one part, MADLAD kept only "- Życzę ci powodzenia."
        var sut = new MachineTranslationTextPreprocessor();

        var batch = sut.Prepare([
            new TranslationSegment(191, "I'm not going along with it.\n- I wish you luck with that.")
        ]);

        Assert.Equal(["I'm not going along with it.", "- I wish you luck with that."], batch.Parts);
        Assert.Equal([1, 1], batch.LineGroups[0]);

        var text = sut.Reassemble(batch, ["Nie zgadzam się na to.", "- Życzę ci powodzenia."]);
        Assert.Equal("Nie zgadzam się na to.\n- Życzę ci powodzenia.", text[191]);
    }

    [Theory]
    [InlineData("I'm not going along with it.\n- I wish you luck with that.", true)]
    [InlineData("- Yes.\n- No.", true)]
    [InlineData("- Yeah. Were you trying to be sarcastic?", false)]
    [InlineData("- I was just a little surprised\nyou didn't tell me about it.", false)]
    [InlineData("You were pretty\nimpressive out there.", false)]
    public void IsDialogueExchange_RecognisesBothDashStyles(string text, bool expected) =>
        Assert.Equal(expected, MachineTranslationTextPreprocessor.IsDialogueExchange(text));
}
