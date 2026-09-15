using NapisyPL.Core.Models;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class SubtitleSynchronizationServiceTests
{
    [Fact]
    public void Analyze_AlignsDifferentlySegmentedCuesWithSmallFixedOffset()
    {
        var reference = new[]
        {
            Cue(101, 10, 11),
            Cue(102, 20, 21),
            Cue(103, 30, 31),
            Cue(104, 40, 41),
            Cue(105, 50, 51),
            Cue(106, 60, 61),
            Cue(107, 70, 71),
            Cue(108, 80, 81),
            Cue(109, 90, 91),
            Cue(110, 100, 101)
        };
        var candidate = new[]
        {
            Cue(8, 10.1, 11.1),
            Cue(19, 20.1, 21.1),
            Cue(27, 40.1, 41.1),
            Cue(38, 50.1, 51.1),
            Cue(42, 60.1, 61.1),
            Cue(57, 80.1, 81.1),
            Cue(63, 90.1, 91.1),
            Cue(79, 100.1, 101.1)
        };

        var analysis = new SubtitleSynchronizationService().Analyze(reference, candidate);

        Assert.True(analysis.Decision == SubtitleSyncDecision.Aligned, analysis.ToString());
        Assert.InRange(analysis.Transform.Scale, 0.999, 1.001);
        Assert.InRange(analysis.Transform.Offset.TotalMilliseconds, -101, -99);
        Assert.InRange(analysis.MatchedCueCoverage, 0.75, 1);
    }

    [Fact]
    public void Analyze_RecognizesStandardFpsRatioAsSafeToSynchronize()
    {
        const double scale = 25d / 23.976d;
        var reference = Timeline(10, 30, 12);
        var candidate = reference
            .Select(cue => Cue(cue.Index + 100, cue.Start.TotalSeconds / scale, cue.End.TotalSeconds / scale))
            .ToArray();

        var analysis = new SubtitleSynchronizationService().Analyze(reference, candidate);

        Assert.Equal(SubtitleSyncDecision.SafeToSynchronize, analysis.Decision);
        Assert.InRange(analysis.Transform.Scale, scale - 0.0001, scale + 0.0001);
        Assert.InRange(analysis.MatchedCueCoverage, 0.75, 1);
        Assert.True(analysis.P90Residual <= TimeSpan.FromMilliseconds(350));
    }

    [Fact]
    public void Analyze_RecognizesTwoSecondShiftAsSafeToSynchronize()
    {
        var reference = Timeline(10, 10, 10);
        var candidate = reference
            .Select(cue => Cue(cue.Index + 100, cue.Start.TotalSeconds + 2, cue.End.TotalSeconds + 2))
            .ToArray();

        var analysis = new SubtitleSynchronizationService().Analyze(reference, candidate);

        Assert.Equal(SubtitleSyncDecision.SafeToSynchronize, analysis.Decision);
        Assert.InRange(analysis.Transform.Scale, 0.999, 1.001);
        Assert.InRange(analysis.Transform.Offset.TotalSeconds, -2.001, -1.999);
    }

    [Fact]
    public void Analyze_RejectsUnrelatedTimelines()
    {
        var reference = Timeline(10, 10, 10);
        var candidate = Timeline(300, 23, 10);

        var analysis = new SubtitleSynchronizationService().Analyze(reference, candidate);

        Assert.Equal(SubtitleSyncDecision.Rejected, analysis.Decision);
    }

    [Fact]
    public void Analyze_RejectsCandidateThatMatchesOnlyAlternateReferenceCues()
    {
        var reference = Timeline(10, 10, 10);
        var candidate = Timeline(10, 20, 5);

        var analysis = new SubtitleSynchronizationService().Analyze(reference, candidate);

        Assert.True(analysis.Decision == SubtitleSyncDecision.Rejected, analysis.ToString());
        Assert.InRange(analysis.MatchedCueCoverage, 0.5, 0.6);
    }

    [Fact]
    public void Apply_ClampsNegativeStartsAndRenumbersCuesInTimelineOrder()
    {
        var candidate = new[]
        {
            new SubtitleCue(19, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11), "second"),
            new SubtitleCue(7, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "first")
        };

        var synchronized = new SubtitleSynchronizationService().Apply(
            candidate,
            new SubtitleTimeTransform(1, TimeSpan.FromSeconds(-4)));

        Assert.Collection(
            synchronized,
            cue =>
            {
                Assert.Equal(1, cue.Index);
                Assert.Equal(TimeSpan.Zero, cue.Start);
                Assert.Equal(TimeSpan.FromSeconds(1), cue.End);
                Assert.Equal("first", cue.Text);
            },
            cue =>
            {
                Assert.Equal(2, cue.Index);
                Assert.Equal(TimeSpan.FromSeconds(6), cue.Start);
                Assert.Equal(TimeSpan.FromSeconds(7), cue.End);
                Assert.Equal("second", cue.Text);
            });
    }

    [Fact]
    public void Analyze_IgnoresTrailingProviderCredits()
    {
        var reference = Timeline(10, 10, 10);
        var dialogue = reference
            .Select(cue => Cue(cue.Index + 100, cue.Start.TotalSeconds + 0.1, cue.End.TotalSeconds + 0.1))
            .ToArray();
        var withCredits = dialogue.Concat(new[]
        {
            Cue(400, 200, 201),
            Cue(401, 205, 206)
        }).ToArray();
        var service = new SubtitleSynchronizationService();

        var baseline = service.Analyze(reference, dialogue);
        var analysis = service.Analyze(reference, withCredits);

        Assert.Equal(baseline.Decision, analysis.Decision);
        Assert.Equal(baseline.MatchedCueCoverage, analysis.MatchedCueCoverage, 3);
        Assert.Equal(baseline.P90Residual, analysis.P90Residual);
    }

    private static SubtitleCue Cue(int index, double startSeconds, double endSeconds) =>
        new(index, TimeSpan.FromSeconds(startSeconds), TimeSpan.FromSeconds(endSeconds), "dialogue");

    private static SubtitleCue[] Timeline(double firstStartSeconds, double spacingSeconds, int count) =>
        Enumerable.Range(1, count)
            .Select(index => Cue(index, firstStartSeconds + (index - 1) * spacingSeconds,
                firstStartSeconds + (index - 1) * spacingSeconds + 1))
            .ToArray();
}
