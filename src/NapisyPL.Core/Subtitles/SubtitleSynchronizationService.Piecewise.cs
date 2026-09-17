using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

/// <summary>One stretch of the candidate that moves by the same transform.</summary>
public sealed record SubtitleSyncSegment(TimeSpan CandidateStart, SubtitleTimeTransform Transform, int MatchedCues);

public sealed record PiecewiseSubtitleSync(
    SubtitleSyncDecision Decision,
    IReadOnlyList<SubtitleSyncSegment> Segments,
    double MatchedCueCoverage,
    TimeSpan P90Residual,
    int DroppedCues,
    IReadOnlyList<SubtitleCue> Cues);

public sealed partial class SubtitleSynchronizationService
{
    private static readonly long OffsetBinTicks = TimeSpan.FromMilliseconds(100).Ticks;
    private static readonly long MaximumOffsetTicks = TimeSpan.FromMinutes(3).Ticks; // recap, intro or cold-open differences
    private static readonly long DurationToleranceTicks = TimeSpan.FromMilliseconds(300).Ticks;
    private static readonly long BoundaryToleranceTicks = TimeSpan.FromMilliseconds(350).Ticks;
    private static readonly long PeakSuppressionTicks = TimeSpan.FromSeconds(1).Ticks;
    private const int MaximumOffsetPeaks = 8;
    private const int MinimumPiecewiseCues = 40;
    private const int MinimumPeakVotes = 8;
    private const int SegmentSwitchPenalty = 5;
    private const int MinimumSegmentMatches = 4;
    private const int MaximumSafeSegments = 8;

    /// <summary>
    /// Aligns subtitles made for another release of the same video (different intro, recap,
    /// cuts or frame rate) to reference cues that match this video, stretch by stretch.
    /// Uses only timing, so the reference can be in another language.
    /// </summary>
    public PiecewiseSubtitleSync AnalyzePiecewise(
        IReadOnlyList<SubtitleCue> reference,
        IReadOnlyList<SubtitleCue> candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!AreValid(reference, requireOrdered: false) || !AreValid(candidate, requireOrdered: false) ||
            candidate.Count < MinimumPiecewiseCues || reference.Count < MinimumPiecewiseCues)
            return new PiecewiseSubtitleSync(SubtitleSyncDecision.Rejected, [], 0, TimeSpan.Zero, 0, []);

        var orderedReference = reference.OrderBy(cue => cue.Start).ToArray();
        var orderedCandidate = candidate.OrderBy(cue => cue.Start).ThenBy(cue => cue.End).ToArray();

        var best = CandidateScales
            .Select(scale => EvaluatePiecewise(orderedReference, orderedCandidate, scale))
            .OrderByDescending(result => result.Matched)
            .ThenBy(result => result.Segments.Count)
            .ThenBy(result => Math.Abs(result.Scale - 1))
            .First();

        var coverage = best.Matched / (double)orderedCandidate.Length;
        var referenceCoverage = best.Matched / (double)orderedReference.Length;
        var p90 = TimeSpan.FromTicks(best.P90ResidualTicks);

        var decision = best.Segments.Count == 0
            ? SubtitleSyncDecision.Rejected
            : coverage >= 0.6 && referenceCoverage >= 0.35 && p90.Ticks <= BoundaryToleranceTicks &&
              best.Segments.Count <= MaximumSafeSegments
                ? IsIdentity(best.Segments)
                    ? SubtitleSyncDecision.Aligned
                    : SubtitleSyncDecision.SafeToSynchronize
                : coverage >= 0.4 && referenceCoverage >= 0.25
                    ? SubtitleSyncDecision.NeedsReview
                    : SubtitleSyncDecision.Rejected;

        return new PiecewiseSubtitleSync(decision, best.Segments, coverage, p90, best.Dropped, best.Cues);
    }

    private static bool IsIdentity(IReadOnlyList<SubtitleSyncSegment> segments) =>
        segments.Count == 1 &&
        Math.Abs(segments[0].Transform.Scale - 1) <= 0.001 &&
        Math.Abs(segments[0].Transform.Offset.TotalMilliseconds) <= 250;

    private static PiecewiseEvaluation EvaluatePiecewise(SubtitleCue[] reference, SubtitleCue[] candidate, double scale)
    {
        var referenceStarts = reference.Select(cue => cue.Start.Ticks).ToArray();
        var referenceEnds = reference.Select(cue => cue.End.Ticks).OrderBy(ticks => ticks).ToArray();
        var scaledStarts = candidate.Select(cue => ScaleTicks(cue.Start.Ticks, scale)).ToArray();
        var scaledEnds = candidate.Select(cue => ScaleTicks(cue.End.Ticks, scale)).ToArray();

        var offsets = FindOffsetPeaks(reference, scaledStarts, scaledEnds);
        if (offsets.Count == 0)
            return PiecewiseEvaluation.Empty(scale);

        // Refine each peak on the cues it explains, then decide which cue belongs to which peak.
        for (var peak = 0; peak < offsets.Count; peak++)
            offsets[peak] = RefineOffset(offsets[peak], scaledStarts, scaledEnds, referenceStarts, referenceEnds);

        var matched = new bool[candidate.Length, offsets.Count];
        for (var position = 0; position < candidate.Length; position++)
        for (var peak = 0; peak < offsets.Count; peak++)
            matched[position, peak] = Residual(scaledStarts[position], scaledEnds[position], offsets[peak], referenceStarts, referenceEnds) is not null;

        var states = AssignSegments(matched, candidate.Length, offsets.Count);
        AbsorbWeakSegments(states, matched);

        var segments = new List<SubtitleSyncSegment>();
        var residuals = new List<long>();
        var cues = new List<SubtitleCue>();
        var matchedTotal = 0;
        var dropped = 0;
        long previousStart = -1;
        long previousEnd = -1;

        var runStart = 0;
        while (runStart < candidate.Length)
        {
            var state = states[runStart];
            var runEnd = runStart;
            while (runEnd + 1 < candidate.Length && states[runEnd + 1] == state)
                runEnd++;

            var runOffset = offsets[state];
            var runMatched = 0;
            for (var position = runStart; position <= runEnd; position++)
            {
                if (Residual(scaledStarts[position], scaledEnds[position], runOffset, referenceStarts, referenceEnds) is { } residual)
                {
                    runMatched++;
                    residuals.Add(residual);
                }
            }

            matchedTotal += runMatched;
            var transform = new SubtitleTimeTransform(scale, TimeSpan.FromTicks(runOffset));
            segments.Add(new SubtitleSyncSegment(candidate[runStart].Start, transform, runMatched));

            for (var position = runStart; position <= runEnd; position++)
            {
                var start = scaledStarts[position] + runOffset;
                var end = scaledEnds[position] + runOffset;
                // A jump back in time means this release has a scene the video does not.
                if (start < 0 || start < previousStart || (position == runStart && start < previousEnd - PeakSuppressionTicks / 2))
                {
                    dropped++;
                    continue;
                }

                cues.Add(new SubtitleCue(cues.Count + 1, TimeSpan.FromTicks(start), TimeSpan.FromTicks(end), candidate[position].Text));
                previousStart = start;
                previousEnd = end;
            }

            runStart = runEnd + 1;
        }

        residuals.Sort();
        var p90 = residuals.Count == 0 ? long.MaxValue : residuals[(int)Math.Ceiling(residuals.Count * 0.9) - 1];
        return new PiecewiseEvaluation(scale, segments, matchedTotal, p90, dropped, cues);
    }

    /// <summary>Votes offsets from cue pairs of similar length; each peak is one candidate stretch.</summary>
    private static List<long> FindOffsetPeaks(SubtitleCue[] reference, long[] scaledStarts, long[] scaledEnds)
    {
        var votes = new Dictionary<long, int>();
        for (var position = 0; position < scaledStarts.Length; position++)
        {
            var duration = scaledEnds[position] - scaledStarts[position];
            foreach (var cue in reference)
            {
                if (Math.Abs(cue.End.Ticks - cue.Start.Ticks - duration) > DurationToleranceTicks)
                    continue;
                var offset = cue.Start.Ticks - scaledStarts[position];
                if (Math.Abs(offset) > MaximumOffsetTicks)
                    continue;
                var bin = (long)Math.Round(offset / (double)OffsetBinTicks);
                votes[bin] = votes.GetValueOrDefault(bin) + 1;
            }
        }

        var smoothed = votes.Keys
            .Select(bin => (Bin: bin, Score: votes.GetValueOrDefault(bin - 1) + votes[bin] + votes.GetValueOrDefault(bin + 1)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => Math.Abs(item.Bin))
            .ToArray();

        var peaks = new List<long>();
        var suppressionBins = PeakSuppressionTicks / OffsetBinTicks;
        foreach (var (bin, score) in smoothed)
        {
            if (score < MinimumPeakVotes || peaks.Count == MaximumOffsetPeaks)
                break;
            if (peaks.Any(peak => Math.Abs(peak / OffsetBinTicks - bin) <= suppressionBins))
                continue;
            peaks.Add(bin * OffsetBinTicks);
        }

        return peaks;
    }

    private static long RefineOffset(long offset, long[] scaledStarts, long[] scaledEnds, long[] referenceStarts, long[] referenceEnds)
    {
        var deltas = new List<double>();
        for (var position = 0; position < scaledStarts.Length; position++)
        {
            if (Residual(scaledStarts[position], scaledEnds[position], offset, referenceStarts, referenceEnds) is null)
                continue;
            deltas.Add(Nearest(referenceStarts, scaledStarts[position] + offset) - scaledStarts[position]);
            deltas.Add(Nearest(referenceEnds, scaledEnds[position] + offset) - scaledEnds[position]);
        }

        return deltas.Count < MinimumMatchedCues * 2 ? offset : (long)Math.Round(Median(deltas.ToArray()));
    }

    /// <summary>The larger of the start and end residuals, or null when either misses.</summary>
    private static long? Residual(long scaledStart, long scaledEnd, long offset, long[] referenceStarts, long[] referenceEnds)
    {
        var start = scaledStart + offset;
        var end = scaledEnd + offset;
        var startResidual = Math.Abs(Nearest(referenceStarts, start) - start);
        var endResidual = Math.Abs(Nearest(referenceEnds, end) - end);
        return startResidual <= BoundaryToleranceTicks && endResidual <= BoundaryToleranceTicks
            ? Math.Max(startResidual, endResidual)
            : null;
    }

    /// <summary>Viterbi over cues: stay on an offset while it explains cues, switch only when it pays.</summary>
    private static int[] AssignSegments(bool[,] matched, int cueCount, int stateCount)
    {
        var cost = new int[stateCount];
        var back = new int[cueCount, stateCount];
        for (var state = 0; state < stateCount; state++)
            cost[state] = matched[0, state] ? 0 : 1;

        for (var position = 1; position < cueCount; position++)
        {
            var bestPrevious = 0;
            for (var state = 1; state < stateCount; state++)
                if (cost[state] < cost[bestPrevious])
                    bestPrevious = state;

            var next = new int[stateCount];
            for (var state = 0; state < stateCount; state++)
            {
                var stay = cost[state];
                var switchCost = cost[bestPrevious] + SegmentSwitchPenalty;
                if (stay <= switchCost)
                {
                    next[state] = stay;
                    back[position, state] = state;
                }
                else
                {
                    next[state] = switchCost;
                    back[position, state] = bestPrevious;
                }

                next[state] += matched[position, state] ? 0 : 1;
            }

            cost = next;
        }

        var states = new int[cueCount];
        var current = 0;
        for (var state = 1; state < stateCount; state++)
            if (cost[state] < cost[current])
                current = state;
        for (var position = cueCount - 1; position >= 0; position--)
        {
            states[position] = current;
            current = back[position, current];
        }

        return states;
    }

    /// <summary>Folds stretches with too few matches into the neighbour before them.</summary>
    private static void AbsorbWeakSegments(int[] states, bool[,] matched)
    {
        var runStart = 0;
        while (runStart < states.Length)
        {
            var runEnd = runStart;
            while (runEnd + 1 < states.Length && states[runEnd + 1] == states[runStart])
                runEnd++;

            var hits = 0;
            for (var position = runStart; position <= runEnd; position++)
                if (matched[position, states[position]])
                    hits++;

            if (hits < MinimumSegmentMatches)
            {
                var replacement = runStart > 0 ? states[runStart - 1] : runEnd + 1 < states.Length ? states[runEnd + 1] : states[runStart];
                for (var position = runStart; position <= runEnd; position++)
                    states[position] = replacement;
            }

            runStart = runEnd + 1;
        }
    }

    private sealed record PiecewiseEvaluation(
        double Scale,
        IReadOnlyList<SubtitleSyncSegment> Segments,
        int Matched,
        long P90ResidualTicks,
        int Dropped,
        IReadOnlyList<SubtitleCue> Cues)
    {
        public static PiecewiseEvaluation Empty(double scale) => new(scale, [], 0, long.MaxValue, 0, []);
    }
}
