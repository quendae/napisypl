using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class SurgicalGenderEditTests
{
    [Fact]
    public void Apply_ReplacesOnlySmallExactFragment()
    {
        var cue = Cue(45, "Nazwałeś ją \"biznatch\"?");
        var edit = new SurgicalGenderEdit(45, "Nazwałeś", "Nazwałaś", 0.96);

        var result = SurgicalGenderEditApplier.TryApply(cue, edit, out var changed);

        Assert.True(result);
        Assert.Equal("Nazwałaś ją \"biznatch\"?", changed.Text);
    }

    [Theory]
    [InlineData("Byłem", "Byłam")]
    [InlineData("gotowy", "gotowa")]
    [InlineData("zrobiłeś", "zrobiłaś")]
    [InlineData("powinieneś", "powinnaś")]
    [InlineData("sam", "sama")]
    public void Apply_AcceptsCommonGenderInflections(string find, string replace)
    {
        var cue = Cue(7, $"{find} tutaj.");
        var edit = new SurgicalGenderEdit(7, find, replace, 0.97);

        Assert.True(SurgicalGenderEditApplier.TryApply(cue, edit, out var changed));
        Assert.Equal($"{replace} tutaj.", changed.Text);
    }

    [Theory]
    [InlineData("Byłem", "Byłam", 0.70, SurgicalGenderEditRejectReason.LowConfidence)]
    [InlineData("Byłeś", "Byłaś", 0.99, SurgicalGenderEditRejectReason.FindMissing)]
    [InlineData("Byłem", "Zrobiłam", 0.99, SurgicalGenderEditRejectReason.NotInflectionOnly)]
    public void Apply_ReportsSpecificRejectReason(
        string find,
        string replace,
        double confidence,
        SurgicalGenderEditRejectReason expectedReason)
    {
        var cue = Cue(7, "Byłem gotowy.");
        var edit = new SurgicalGenderEdit(7, find, replace, confidence);

        var result = SurgicalGenderEditApplier.TryApply(cue, edit, out var changed, out var reason);

        Assert.False(result);
        Assert.Equal(cue, changed);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void Apply_RejectsWholeSentenceRewrite()
    {
        var cue = Cue(45, "Nazwałeś ją \"biznatch\"?");
        var edit = new SurgicalGenderEdit(45, "Nazwałeś ją \"biznatch\"?", "Za to, co zrobiłeś...", 0.99);

        var result = SurgicalGenderEditApplier.TryApply(cue, edit, out var changed);

        Assert.False(result);
        Assert.Equal(cue.Text, changed.Text);
    }

    [Fact]
    public void Apply_RejectsUnrelatedShortReplacement()
    {
        var cue = Cue(45, "Nazwałeś ją \"biznatch\"?");
        var edit = new SurgicalGenderEdit(45, "Nazwałeś", "Zrobiłeś", 0.99);

        Assert.False(SurgicalGenderEditApplier.TryApply(cue, edit, out _));
    }

    [Fact]
    public void Apply_RejectsSameStemPersonChange()
    {
        var cue = Cue(45, "Nazwałeś ją \"biznatch\"?");
        var edit = new SurgicalGenderEdit(45, "Nazwałeś", "Nazwałem", 0.99);

        Assert.False(SurgicalGenderEditApplier.TryApply(cue, edit, out _));
    }

    [Fact]
    public void Apply_RejectsLowConfidenceOrMissingFragment()
    {
        var cue = Cue(7, "Byłem gotowy.");

        Assert.False(SurgicalGenderEditApplier.TryApply(cue, new SurgicalGenderEdit(7, "Byłem", "Byłam", 0.70), out _));
        Assert.False(SurgicalGenderEditApplier.TryApply(cue, new SurgicalGenderEdit(7, "Byłeś", "Byłaś", 0.99), out _));
    }

    [Fact]
    public void ParseResponse_ReadsFindReplaceConfidenceAndTarget()
    {
        var edits = SurgicalGenderEditProtocol.ParseResponse("""
            [{"id":7,"find":"Byłem","replace":"Byłam","confidence":0.94,"target":"speaker"}]
            """);

        var edit = Assert.Single(edits);
        Assert.Equal(7, edit.Id);
        Assert.Equal("Byłem", edit.Find);
        Assert.Equal("Byłam", edit.Replace);
        Assert.Equal(0.94, edit.Confidence, 3);
        Assert.Equal(GenderAgreementTarget.Speaker, edit.Target);
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);
}
