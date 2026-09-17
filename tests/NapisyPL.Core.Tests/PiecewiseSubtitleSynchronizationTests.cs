using NapisyPL.Core.Models;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class PiecewiseSubtitleSynchronizationTests
{
    [Fact]
    public void OtherReleaseWithRecapExtraSceneAndFrameRateIsMappedBackOntoTheVideo()
    {
        var reference = BuildReference(600, seed: 7);
        var candidate = BuildOtherRelease(reference, seed: 11, out var expectedStarts);

        var service = new SubtitleSynchronizationService();
        var global = service.Analyze(reference, candidate).Decision;
        Assert.True(global is not (SubtitleSyncDecision.Aligned or SubtitleSyncDecision.SafeToSynchronize), $"global {global}");

        var result = service.AnalyzePiecewise(reference, candidate);

        Assert.Equal(SubtitleSyncDecision.SafeToSynchronize, result.Decision);
        Assert.InRange(result.Segments.Count, 2, 4);
        Assert.All(result.Segments, segment => Assert.InRange(segment.Transform.Scale, 25d / 23.976d - 0.0001, 25d / 23.976d + 0.0001));

        var placed = 0;
        foreach (var cue in result.Cues)
        {
            if (!expectedStarts.TryGetValue(cue.Text, out var expected))
                continue;
            placed++;
            Assert.InRange(Math.Abs((cue.Start - expected).TotalMilliseconds), 0, 400);
        }

        Assert.True(placed >= 540, $"placed {placed}");
        Assert.DoesNotContain(result.Cues, cue => cue.Text.StartsWith("recap", StringComparison.Ordinal) && cue.Start < TimeSpan.Zero);
        Assert.True(result.Cues.Zip(result.Cues.Skip(1)).All(pair => pair.First.Start <= pair.Second.Start));
    }

    [Fact]
    public void SameReleaseIsReportedAsAligned()
    {
        var reference = BuildReference(300, seed: 3);
        var candidate = reference.Select(cue => cue with { Text = "pl " + cue.Text }).ToArray();

        var result = new SubtitleSynchronizationService().AnalyzePiecewise(reference, candidate);

        Assert.Equal(SubtitleSyncDecision.Aligned, result.Decision);
        Assert.Single(result.Segments);
        Assert.Equal(300, result.Cues.Count);
    }

    [Fact]
    public void UnrelatedSubtitlesAreRejected()
    {
        var reference = BuildReference(400, seed: 5);
        var unrelated = BuildReference(400, seed: 99);

        var result = new SubtitleSynchronizationService().AnalyzePiecewise(reference, unrelated);

        Assert.NotEqual(SubtitleSyncDecision.SafeToSynchronize, result.Decision);
        Assert.NotEqual(SubtitleSyncDecision.Aligned, result.Decision);
    }

    private static SubtitleCue[] BuildReference(int count, int seed)
    {
        var random = new Random(seed);
        var cues = new SubtitleCue[count];
        var time = TimeSpan.FromSeconds(20);
        for (var index = 0; index < count; index++)
        {
            time += TimeSpan.FromMilliseconds(random.Next(150, 6000));
            var duration = TimeSpan.FromMilliseconds(random.Next(900, 4200));
            cues[index] = new SubtitleCue(index + 1, time, time + duration, "line " + index);
            time += duration;
        }
        return cues;
    }

    /// <summary>
    /// A 23.976 fps release of a 25 fps video: 40 s recap at the start, a 14 s scene after cue 250
    /// that the video does not have, timing jitter, and a few cues merged by its translator.
    /// </summary>
    private static SubtitleCue[] BuildOtherRelease(SubtitleCue[] reference, int seed, out Dictionary<string, TimeSpan> expectedStarts)
    {
        var random = new Random(seed);
        var scale = 23.976d / 25d;
        var cues = new List<(TimeSpan Start, TimeSpan End, string Text)>();
        expectedStarts = [];

        for (var index = 0; index < 8; index++)
        {
            var start = TimeSpan.FromSeconds(2 + index * 4.5);
            cues.Add((start, start + TimeSpan.FromSeconds(3), "recap " + index));
        }

        var shift = TimeSpan.FromSeconds(40);
        for (var index = 0; index < reference.Length; index++)
        {
            if (index == 250)
            {
                var sceneStart = reference[index - 1].End + shift + TimeSpan.FromMilliseconds(500);
                for (var extra = 0; extra < 3; extra++)
                    cues.Add((sceneStart + TimeSpan.FromSeconds(extra * 4), sceneStart + TimeSpan.FromSeconds(extra * 4 + 3), "extra " + extra));
                shift += TimeSpan.FromSeconds(14);
            }

            var cue = reference[index];
            if (index % 37 == 5 && index + 1 < reference.Length && index != 249)
            {
                // Merged with the next line: its boundaries no longer match the reference.
                var next = reference[index + 1];
                cues.Add((cue.Start + shift, next.End + shift, "merged " + index));
                index++;
                continue;
            }

            var jitterStart = TimeSpan.FromMilliseconds(random.Next(-120, 121));
            var jitterEnd = TimeSpan.FromMilliseconds(random.Next(-120, 121));
            cues.Add((cue.Start + shift + jitterStart, cue.End + shift + jitterEnd, "pl " + index));
            expectedStarts["pl " + index] = cue.Start;
        }

        return cues
            .Select((cue, position) => new SubtitleCue(
                position + 1,
                TimeSpan.FromTicks((long)(cue.Start.Ticks * scale)),
                TimeSpan.FromTicks((long)(cue.End.Ticks * scale)),
                cue.Text))
            .ToArray();
    }
}
