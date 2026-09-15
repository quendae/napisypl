using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerSegment(double StartSeconds, double EndSeconds, int Speaker);

public static class SpeakerCueMapper
{
    private const double MinimumOverlapSeconds = 0.20;
    private const double MinimumCueOverlapRatio = 0.20;
    private const double MinimumWinnerMarginSeconds = 0.15;
    private const double MinimumWinnerMarginRatio = 0.15;

    public static IReadOnlyDictionary<int, string?> Map(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyList<SpeakerSegment> segments)
    {
        var result = new Dictionary<int, string?>(cues.Count);

        foreach (var cue in cues)
        {
            var cueStart = cue.Start.TotalSeconds;
            var cueEnd = cue.End.TotalSeconds;

            var overlapBySpeaker = new Dictionary<int, double>();
            foreach (var segment in segments)
            {
                var overlap = Math.Max(0, Math.Min(cueEnd, segment.EndSeconds) - Math.Max(cueStart, segment.StartSeconds));
                if (overlap > 0)
                    overlapBySpeaker[segment.Speaker] = overlapBySpeaker.GetValueOrDefault(segment.Speaker) + overlap;
            }

            var ranked = overlapBySpeaker
                .OrderByDescending(pair => pair.Value)
                .ToArray();
            var cueDuration = Math.Max(0, cueEnd - cueStart);
            var hasWinner = ranked.Length > 0 &&
                ranked[0].Value >= MinimumOverlapSeconds &&
                cueDuration > 0 &&
                ranked[0].Value / cueDuration >= MinimumCueOverlapRatio &&
                (ranked.Length == 1 ||
                 ranked[0].Value - ranked[1].Value >= Math.Max(
                     MinimumWinnerMarginSeconds,
                     ranked[0].Value * MinimumWinnerMarginRatio));

            result[cue.Index] = hasWinner
                ? $"SPEAKER_{ranked[0].Key:00}"
                : null;
        }

        return result;
    }
}
