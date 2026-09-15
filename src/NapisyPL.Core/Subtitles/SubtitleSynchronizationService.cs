using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

public enum SubtitleSyncDecision
{
    Aligned,
    SafeToSynchronize,
    NeedsReview,
    Rejected
}

public sealed record SubtitleTimeTransform(double Scale, TimeSpan Offset)
{
    public static SubtitleTimeTransform Identity { get; } = new(1, TimeSpan.Zero);
}

public sealed record SubtitleTimingAnalysis(
    SubtitleSyncDecision Decision,
    SubtitleTimeTransform Transform,
    double MatchedCueCoverage,
    TimeSpan MedianResidual,
    TimeSpan P90Residual);

/// <summary>Analyzes cue timing independently from cue numbering and text.</summary>
public sealed class SubtitleSynchronizationService
{
    private static readonly double[] CandidateScales = [1, 24d / 25d, 23.976d / 25d, 25d / 24d, 25d / 23.976d];
    private static readonly TimeSpan InitialResidualWindow = TimeSpan.FromSeconds(1);
    private const int MinimumMatchedCues = 4;
    private const int MinimumReviewMatchedCues = 8;

    public SubtitleTimingAnalysis Analyze(
        IReadOnlyList<SubtitleCue> reference,
        IReadOnlyList<SubtitleCue> candidate)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);

        if (!AreValid(reference, requireOrdered: true) || !AreValid(candidate, requireOrdered: true))
            return Rejected();

        var referenceStarts = reference.Select(cue => cue.Start.Ticks).ToArray();
        var referenceEnds = reference.Select(cue => cue.End.Ticks).OrderBy(ticks => ticks).ToArray();
        var evaluations = CandidateScales
            .Select(scale => Evaluate(referenceStarts, referenceEnds, candidate, scale))
            .ToArray();
        var best = evaluations
            .OrderByDescending(evaluation => evaluation.CompleteMatchedCueCount)
            .ThenByDescending(evaluation => evaluation.Coverage)
            .ThenBy(evaluation => evaluation.P90ResidualTicks)
            .ThenBy(evaluation => Math.Abs(evaluation.Transform.Scale - 1))
            .First();

        if (!best.IsTransformedTimelineValid || best.CompleteMatchedCueCount < MinimumMatchedCues)
            return Rejected(best.Transform);

        var medianResidual = TimeSpan.FromTicks(best.MedianResidualTicks);
        var p90Residual = TimeSpan.FromTicks(best.P90ResidualTicks);
        var decision = best.Coverage >= 0.75 && p90Residual <= TimeSpan.FromMilliseconds(350)
            ? Math.Abs(best.Transform.Scale - 1) <= 0.001 && Math.Abs(best.Transform.Offset.TotalMilliseconds) <= 250
                ? SubtitleSyncDecision.Aligned
                : IsNearKnownScale(best.Transform.Scale) ? SubtitleSyncDecision.SafeToSynchronize : SubtitleSyncDecision.Rejected
            : best.CompleteMatchedCueCount >= MinimumReviewMatchedCues &&
              best.Coverage >= 0.5 && p90Residual <= TimeSpan.FromMilliseconds(750)
                ? SubtitleSyncDecision.NeedsReview
                : SubtitleSyncDecision.Rejected;

        return new SubtitleTimingAnalysis(decision, best.Transform, best.Coverage, medianResidual, p90Residual);
    }

    public IReadOnlyList<SubtitleCue> Apply(IReadOnlyList<SubtitleCue> candidate, SubtitleTimeTransform transform)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(transform);
        if (!AreValid(candidate, requireOrdered: false))
            throw new ArgumentException("Subtitle cues must have non-negative, positive durations.", nameof(candidate));
        if (transform.Scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(transform), "The timing scale must be positive.");

        return candidate
            .Select((cue, position) =>
            {
                var start = TimeSpan.FromTicks(Math.Max(0, TransformTicks(cue.Start.Ticks, transform)));
                var duration = Math.Max(1, (long)Math.Round((cue.End.Ticks - cue.Start.Ticks) * transform.Scale));
                return new AppliedCue(position, start, start.Add(TimeSpan.FromTicks(duration)), cue.Text);
            })
            .OrderBy(cue => cue.Start)
            .ThenBy(cue => cue.End)
            .ThenBy(cue => cue.Position)
            .Select((cue, index) => new SubtitleCue(index + 1, cue.Start, cue.End, cue.Text))
            .ToArray();
    }

    private static Evaluation Evaluate(
        long[] referenceStarts,
        long[] referenceEnds,
        IReadOnlyList<SubtitleCue> candidate,
        double initialScale)
    {
        var initial = new SubtitleTimeTransform(initialScale, EstimateOffset(referenceStarts, referenceEnds, candidate, initialScale));
        var matches = Match(referenceStarts, referenceEnds, candidate, initial);
        var transform = matches.CompleteMatchedCueCount >= MinimumMatchedCues
            ? FitAffine(matches.Events, initial)
            : initial;
        matches = Match(referenceStarts, referenceEnds, candidate, transform);

        var firstMatch = Array.FindIndex(matches.CompleteCueMatches, match => match);
        var lastMatch = Array.FindLastIndex(matches.CompleteCueMatches, match => match);
        if (firstMatch < 0)
            return new Evaluation(transform, 0, 0, long.MaxValue, long.MaxValue, false);

        var relevant = matches.CompleteCueMatches[firstMatch..(lastMatch + 1)];
        var candidateCoverage = matches.CompleteMatchedCueCount / (double)relevant.Length;
        var referenceCoverage = Math.Min(
            CalculateReferenceCoverage(matches.Starts),
            CalculateReferenceCoverage(matches.Ends));
        var residuals = matches.Events
            .Where(match => matches.CompleteCueMatches[match.CuePosition])
            .Select(match => match.ResidualTicks)
            .OrderBy(residual => residual)
            .ToArray();
        var median = residuals.Length == 0 ? long.MaxValue : (long)Math.Round(Median(residuals.Select(value => (double)value).ToArray()));
        var p90 = residuals.Length == 0 ? long.MaxValue : residuals[(int)Math.Ceiling(residuals.Length * 0.9) - 1];
        var transformedValid = candidate.All(cue =>
        {
            var start = TransformTicks(cue.Start.Ticks, transform);
            var end = TransformTicks(cue.End.Ticks, transform);
            return start >= 0 && end > start;
        });
        return new Evaluation(
            transform,
            matches.CompleteMatchedCueCount,
            Math.Min(candidateCoverage, referenceCoverage),
            median,
            p90,
            transformedValid);
    }

    private static TimeSpan EstimateOffset(
        long[] referenceStarts,
        long[] referenceEnds,
        IReadOnlyList<SubtitleCue> candidate,
        double scale)
    {
        var offsets = candidate
            .SelectMany(cue => new[]
            {
                Nearest(referenceStarts, ScaleTicks(cue.Start.Ticks, scale)) - ScaleTicks(cue.Start.Ticks, scale),
                Nearest(referenceEnds, ScaleTicks(cue.End.Ticks, scale)) - ScaleTicks(cue.End.Ticks, scale)
            })
            .Select(value => (double)value)
            .ToArray();
        return TimeSpan.FromTicks((long)Math.Round(Median(offsets)));
    }

    private static MatchSet Match(
        long[] referenceStarts,
        long[] referenceEnds,
        IReadOnlyList<SubtitleCue> candidate,
        SubtitleTimeTransform transform)
    {
        var starts = MatchBoundaryType(candidate.Select((cue, position) => new Boundary(position, cue.Start.Ticks)), referenceStarts, transform);
        var ends = MatchBoundaryType(candidate.Select((cue, position) => new Boundary(position, cue.End.Ticks)), referenceEnds, transform);
        var matchedStarts = starts.Select(match => match.CuePosition).ToHashSet();
        var matchedEnds = ends.Select(match => match.CuePosition).ToHashSet();
        var complete = Enumerable.Range(0, candidate.Count)
            .Select(position => matchedStarts.Contains(position) && matchedEnds.Contains(position))
            .ToArray();
        return new MatchSet(starts, ends, complete, complete.Count(match => match));
    }

    private static IReadOnlyList<BoundaryMatch> MatchBoundaryType(
        IEnumerable<Boundary> candidateBoundaries,
        long[] referenceBoundaries,
        SubtitleTimeTransform transform)
    {
        var matches = new List<BoundaryMatch>();
        var nextReference = 0;
        foreach (var candidate in candidateBoundaries.OrderBy(boundary => boundary.Ticks))
        {
            if (nextReference >= referenceBoundaries.Length)
                break;

            var transformed = TransformTicks(candidate.Ticks, transform);
            while (nextReference + 1 < referenceBoundaries.Length && referenceBoundaries[nextReference + 1] <= transformed)
                nextReference++;

            var chosen = ChooseNearestAvailable(referenceBoundaries, nextReference, transformed);
            var residual = Math.Abs(referenceBoundaries[chosen] - transformed);
            if (residual <= InitialResidualWindow.Ticks)
            {
                matches.Add(new BoundaryMatch(candidate.CuePosition, chosen, candidate.Ticks, referenceBoundaries[chosen], residual));
                nextReference = chosen + 1;
            }
            else if (referenceBoundaries[nextReference] < transformed)
            {
                nextReference++;
            }
        }
        return matches;
    }

    private static SubtitleTimeTransform FitAffine(IReadOnlyList<BoundaryMatch> matches, SubtitleTimeTransform fallback)
    {
        if (matches.Count < MinimumMatchedCues * 2)
            return fallback;

        var meanX = matches.Average(match => (double)match.CandidateTicks);
        var meanY = matches.Average(match => (double)match.ReferenceTicks);
        var variance = matches.Sum(match => Math.Pow(match.CandidateTicks - meanX, 2));
        if (variance <= 0)
            return fallback;

        var covariance = matches.Sum(match => (match.CandidateTicks - meanX) * (match.ReferenceTicks - meanY));
        var scale = covariance / variance;
        if (scale <= 0)
            return fallback;
        var offset = meanY - scale * meanX;
        return new SubtitleTimeTransform(scale, TimeSpan.FromTicks((long)Math.Round(offset)));
    }

    private static int ChooseNearestAvailable(long[] values, int firstAvailable, long target)
    {
        if (firstAvailable + 1 == values.Length)
            return firstAvailable;
        return target - values[firstAvailable] <= values[firstAvailable + 1] - target
            ? firstAvailable
            : firstAvailable + 1;
    }

    private static bool IsNearKnownScale(double scale) =>
        CandidateScales.Any(candidateScale => Math.Abs(scale - candidateScale) <= 0.005);

    private static long ScaleTicks(long ticks, double scale) => (long)Math.Round(ticks * scale);

    private static double CalculateReferenceCoverage(IReadOnlyList<BoundaryMatch> matches)
    {
        if (matches.Count == 0)
            return 0;
        var first = matches.Min(match => match.ReferencePosition);
        var last = matches.Max(match => match.ReferencePosition);
        return matches.Count / (double)(last - first + 1);
    }

    private static long TransformTicks(long ticks, SubtitleTimeTransform transform) =>
        (long)Math.Round(ticks * transform.Scale + transform.Offset.Ticks);

    private static long Nearest(long[] values, long target)
    {
        var position = Array.BinarySearch(values, target);
        if (position >= 0)
            return values[position];

        position = ~position;
        if (position == 0)
            return values[0];
        if (position == values.Length)
            return values[^1];
        return target - values[position - 1] <= values[position] - target ? values[position - 1] : values[position];
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        var middle = values.Length / 2;
        return values.Length % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    private static bool AreValid(IReadOnlyList<SubtitleCue> cues, bool requireOrdered) =>
        cues.Count > 0 && cues.All(cue => cue.Start >= TimeSpan.Zero && cue.End > cue.Start) &&
        (!requireOrdered || cues.Zip(cues.Skip(1), (previous, current) => previous.Start <= current.Start).All(ordered => ordered));

    private static SubtitleTimingAnalysis Rejected(SubtitleTimeTransform? transform = null) =>
        new(SubtitleSyncDecision.Rejected, transform ?? SubtitleTimeTransform.Identity, 0, TimeSpan.Zero, TimeSpan.Zero);

    private sealed record Boundary(int CuePosition, long Ticks);
    private sealed record BoundaryMatch(int CuePosition, int ReferencePosition, long CandidateTicks, long ReferenceTicks, long ResidualTicks);
    private sealed record MatchSet(
        IReadOnlyList<BoundaryMatch> Starts,
        IReadOnlyList<BoundaryMatch> Ends,
        bool[] CompleteCueMatches,
        int CompleteMatchedCueCount)
    {
        public IReadOnlyList<BoundaryMatch> Events => Starts.Concat(Ends).ToArray();
    }
    private sealed record Evaluation(
        SubtitleTimeTransform Transform,
        int CompleteMatchedCueCount,
        double Coverage,
        long MedianResidualTicks,
        long P90ResidualTicks,
        bool IsTransformedTimelineValid);
    private sealed record AppliedCue(int Position, TimeSpan Start, TimeSpan End, string Text);
}
