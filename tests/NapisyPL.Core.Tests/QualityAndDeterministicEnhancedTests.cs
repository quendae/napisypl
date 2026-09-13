using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;
using NapisyPL.Core.OfflineMt.Nllb;

namespace NapisyPL.Core.Tests;

public sealed class QualityAndDeterministicEnhancedTests
{
    [Fact]
    public void QualityNllb3_3B_UsesPinnedOfficialThreeShardModel()
    {
        var descriptor = NllbModelDescriptor.ForProfile(NllbModelProfile.QualityNllb3_3B);

        Assert.Equal("facebook/nllb-200-3.3B", descriptor.ModelId);
        Assert.Equal("1a07f7d195896b2114afcb79b7b57ab512e7b43e", descriptor.Revision);
        Assert.Equal("CC-BY-NC-4.0", descriptor.LicenseId);
        Assert.True(descriptor.BenchmarkOnly);
        Assert.Contains(descriptor.Files, file => file.RelativePath == "pytorch_model-00001-of-00003.bin");
        Assert.Contains(descriptor.Files, file => file.RelativePath == "pytorch_model-00002-of-00003.bin");
        Assert.Contains(descriptor.Files, file => file.RelativePath == "pytorch_model-00003-of-00003.bin");
        Assert.Contains(descriptor.Files, file => file.RelativePath == "pytorch_model.bin.index.json");
    }

    [Fact]
    public void Review_WhenMaleSpeakerAddressesFemaleNextResponder_ChangesOnlySecondPersonGender()
    {
        var source = new[]
        {
            Cue(1, 0.0, 1.0, "Did you do it?"),
            Cue(2, 1.2, 2.0, "Yes, I did.")
        };
        var translated = new[]
        {
            Cue(1, 0.0, 1.0, "Zrobiłeś to?"),
            Cue(2, 1.2, 2.0, "Tak, zrobiłam.")
        };
        var speakers = Speakers((1, "M"), (2, "F"));
        var evidence = Evidence(("M", SpeakerVoiceGender.Male), ("F", SpeakerVoiceGender.Female));

        var result = new DeterministicGenderReviewService().Review(source, translated, speakers, evidence);

        Assert.Equal("Zrobiłaś to?", result[0].Text);
        Assert.Equal("Tak, zrobiłam.", result[1].Text);
    }

    [Fact]
    public void Review_WhenFemaleSpeakerUsesMasculineFirstPerson_ChangesSpeakerAgreement()
    {
        var source = new[] { Cue(1, 0, 1, "I wanted to go.") };
        var translated = new[] { Cue(1, 0, 1, "Chciałem iść.") };
        var speakers = Speakers((1, "F"));
        var evidence = Evidence(("F", SpeakerVoiceGender.Female));

        var result = new DeterministicGenderReviewService().Review(source, translated, speakers, evidence);

        Assert.Equal("Chciałam iść.", result[0].Text);
    }

    [Fact]
    public void Review_WhenMaleAddresseeGetsIrregularFemaleForm_UsesIrregularLexicon()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "You got married?"),
            Cue(2, 1.2, 2, "Yeah.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Wyszłaś za mąż?"),
            Cue(2, 1.2, 2, "Tak.")
        };
        var speakers = Speakers((1, "M1"), (2, "M2"));
        var evidence = Evidence(("M1", SpeakerVoiceGender.Male), ("M2", SpeakerVoiceGender.Male));

        var result = new DeterministicGenderReviewService().Review(source, translated, speakers, evidence);

        Assert.Equal("Wyszedłeś za mąż?", result[0].Text);
    }

    [Fact]
    public void Review_WhenSecondPersonAddresseeIsUnresolved_LeavesTextUnchanged()
    {
        var source = new[]
        {
            Cue(1, 0, 1, "Did you do it?"),
            Cue(2, 1.2, 2, "Come on, we have to go.")
        };
        var translated = new[]
        {
            Cue(1, 0, 1, "Zrobiłeś to?"),
            Cue(2, 1.2, 2, "Chodź, musimy iść.")
        };
        var speakers = Speakers((1, "M"), (2, "M"));
        var evidence = Evidence(("M", SpeakerVoiceGender.Male));

        var result = new DeterministicGenderReviewService().Review(source, translated, speakers, evidence);

        Assert.Equal("Zrobiłeś to?", result[0].Text);
    }

    [Fact]
    public void Review_PreservesCapitalizationMarkupAndPunctuation()
    {
        var source = new[] { Cue(1, 0, 1, "I did it!") };
        var translated = new[] { Cue(1, 0, 1, "<i>ZROBIŁEM!</i>") };
        var speakers = Speakers((1, "F"));
        var evidence = Evidence(("F", SpeakerVoiceGender.Female));

        var result = new DeterministicGenderReviewService().Review(source, translated, speakers, evidence);

        Assert.Equal("<i>ZROBIŁAM!</i>", result[0].Text);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static IReadOnlyDictionary<int, string?> Speakers(params (int Id, string Speaker)[] items) =>
        items.ToDictionary(item => item.Id, item => (string?)item.Speaker);

    private static IReadOnlyDictionary<string, SpeakerGenderEvidence> Evidence(
        params (string Speaker, SpeakerVoiceGender Gender)[] items) =>
        items.ToDictionary(
            item => item.Speaker,
            item => new SpeakerGenderEvidence(item.Gender, 0.98, 3),
            StringComparer.Ordinal);
}
