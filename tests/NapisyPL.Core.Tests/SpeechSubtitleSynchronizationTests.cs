using NapisyPL.Core.Models;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class SpeechSubtitleSynchronizationTests
{
    [Fact]
    public void OtherReleaseIsPlacedOnTheVideosSpeech()
    {
        var random = new Random(21);
        var video = BuildTimeline(500, random);
        // Speech starts a little after each cue appears and stops before it disappears.
        var speech = video
            .Select(cue => new SpeechSpan(cue.Start + TimeSpan.FromMilliseconds(random.Next(0, 250)),
                cue.End - TimeSpan.FromMilliseconds(random.Next(200, 600))))
            .Concat(Enumerable.Range(0, 40).Select(index => new SpeechSpan(TimeSpan.FromSeconds(index * 97.3), TimeSpan.FromSeconds(index * 97.3 + 0.8))))
            .ToArray();

        var scale = 23.976d / 25d;
        var candidate = video
            .Select(cue =>
            {
                var shift = cue.Index > 220 ? TimeSpan.FromSeconds(33) : TimeSpan.FromSeconds(21);
                return new SubtitleCue(cue.Index,
                    TimeSpan.FromTicks((long)((cue.Start + shift).Ticks * scale)),
                    TimeSpan.FromTicks((long)((cue.End + shift).Ticks * scale)),
                    "pl " + cue.Index);
            })
            .ToArray();

        var result = new SubtitleSynchronizationService().AnalyzeAgainstSpeech(speech, candidate);

        Assert.Equal(SubtitleSyncDecision.SafeToSynchronize, result.Decision);
        var expected = video.ToDictionary(cue => "pl " + cue.Index, cue => cue.Start);
        var close = result.Cues.Count(cue => Math.Abs((cue.Start - expected[cue.Text]).TotalMilliseconds) <= 300);
        Assert.True(close >= 470, $"close {close} of {result.Cues.Count}");
    }

    [Fact]
    public void SubtitlesForAnotherVideoAreRejected()
    {
        var random = new Random(4);
        var speech = BuildTimeline(500, random).Select(cue => new SpeechSpan(cue.Start, cue.End)).ToArray();
        var unrelated = BuildTimeline(500, new Random(77));

        var result = new SubtitleSynchronizationService().AnalyzeAgainstSpeech(speech, unrelated);

        Assert.NotEqual(SubtitleSyncDecision.SafeToSynchronize, result.Decision);
    }

    private static SubtitleCue[] BuildTimeline(int count, Random random)
    {
        var cues = new SubtitleCue[count];
        var time = TimeSpan.FromSeconds(30);
        for (var index = 0; index < count; index++)
        {
            time += TimeSpan.FromMilliseconds(random.Next(300, 7000));
            var duration = TimeSpan.FromMilliseconds(random.Next(1200, 4000));
            cues[index] = new SubtitleCue(index + 1, time, time + duration, "line " + index);
            time += duration;
        }
        return cues;
    }
}
