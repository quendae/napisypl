using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class GenderReviewCandidateSelectorTests
{
    [Fact]
    public void SelectCandidateIds_FindsGenderSensitivePolishForms()
    {
        var translated = new[]
        {
            Cue(1, "Byłem gotowy."),
            Cue(2, "Dobrze."),
            Cue(3, "Powiedziałam ci prawdę."),
            Cue(4, "Samochód jest czerwony."),
            Cue(5, "Jesteś zmęczona?")
        };

        var result = GenderReviewCandidateSelector.SelectCandidateIds(translated);

        Assert.Contains(1, result);
        Assert.Contains(3, result);
        Assert.Contains(5, result);
        Assert.DoesNotContain(2, result);
        Assert.DoesNotContain(4, result);
    }

    [Fact]
    public void BuildContextWindows_AddsNeighboursAndMergesOverlaps()
    {
        var cues = Enumerable.Range(1, 12).Select(i => Cue(i, $"Linia {i}")).ToArray();

        var windows = GenderReviewCandidateSelector.BuildContextWindows(cues, new HashSet<int> { 5, 7 }, radius: 2);

        var window = Assert.Single(windows);
        Assert.Equal(new[] { 3, 4, 5, 6, 7, 8, 9 }, window.Select(c => c.Index).ToArray());
    }

    [Fact]
    public void BuildContextWindows_DoesNotCreateHugeMergedPrompts()
    {
        var cues = Enumerable.Range(1, 50).Select(i => Cue(i, $"Linia {i}")).ToArray();
        var candidateIds = Enumerable.Range(5, 36).Where(i => i % 2 == 1).ToHashSet();

        var windows = GenderReviewCandidateSelector.BuildContextWindows(cues, candidateIds, radius: 3, maxWindowCues: 14);

        Assert.NotEmpty(windows);
        Assert.All(windows, window => Assert.InRange(window.Count, 1, 14));
        foreach (var candidateId in candidateIds)
            Assert.Contains(windows, window => window.Any(cue => cue.Index == candidateId));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);
}
