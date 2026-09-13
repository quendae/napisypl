using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerSegment(double StartSeconds, double EndSeconds, int Speaker);

public static class SpeakerCueMapper
{
    public static IReadOnlyDictionary<int, string?> Map(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<SpeakerSegment> segments)
    {
        var result = new Dictionary<int, string?>(cues.Count);

        foreach (var cue in cues)
        {
            var cueStart = cue.Start.TotalSeconds;
            var cueEnd = cue.End.TotalSeconds;

            SpeakerSegment? best = null;
            var bestOverlap = 0d;
            foreach (var segment in segments)
            {
                var overlap = Math.Max(0, Math.Min(cueEnd, segment.EndSeconds) - Math.Max(cueStart, segment.StartSeconds));
                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    best = segment;
                }
            }

            result[cue.Index] = best is null || bestOverlap <= 0
                ? null
                : $"SPEAKER_{best.Speaker:00}";
        }

        return result;
    }
}
