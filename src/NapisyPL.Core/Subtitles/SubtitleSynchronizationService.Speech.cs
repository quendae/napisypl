using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

/// <summary>A stretch of the video's audio where somebody speaks.</summary>
public sealed record SpeechSpan(TimeSpan Start, TimeSpan End);

public sealed partial class SubtitleSynchronizationService
{
    private static readonly long SpeechOnsetToleranceTicks = TimeSpan.FromMilliseconds(300).Ticks;
    private static readonly TimeSpan[] SpeechDecoyShifts =
    [
        TimeSpan.FromSeconds(-9.7), TimeSpan.FromSeconds(-4.3), TimeSpan.FromSeconds(-2.1),
        TimeSpan.FromSeconds(2.9), TimeSpan.FromSeconds(5.1), TimeSpan.FromSeconds(11.9)
    ];
    private const int MinimumSpeechCues = 60;

    /// <summary>
    /// Aligns subtitles to when people start speaking in this video. Speech covers most of a
    /// film, so "the cue lies in speech" says little; a cue appearing within 0.3 s of a speech
    /// onset does, because shifted cues rarely do. The fit must beat the same cues shifted
    /// by a few seconds by a wide margin.
    /// </summary>
    public PiecewiseSubtitleSync AnalyzeAgainstSpeech(
        IReadOnlyList<SpeechSpan> speech,
        IReadOnlyList<SubtitleCue> candidate)
    {
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(candidate);

        var rejected = new PiecewiseSubtitleSync(SubtitleSyncDecision.Rejected, [], 0, TimeSpan.Zero, 0, []);
        LastSpeechDiagnostics = (0, 0);
        if (speech.Count < MinimumSpeechCues || candidate.Count < MinimumSpeechCues || !AreValid(candidate, requireOrdered: false))
            return rejected;

        var onsets = speech.Select(span => span.Start.Ticks).Order().ToArray();
        var ordered = candidate.OrderBy(cue => cue.Start).ThenBy(cue => cue.End).ToArray();

        SpeechEvaluation? best = null;
        foreach (var scale in CandidateScales)
        {
            var evaluation = EvaluateSpeech(onsets, ordered, scale);
            if (best is null || evaluation.Matched - evaluation.DecoyMatched > best.Matched - best.DecoyMatched)
                best = evaluation;
        }

        if (best is null || best.Segments.Count == 0)
            return rejected;

        var coverage = best.Matched / (double)ordered.Length;
        var decoy = best.DecoyMatched / (double)ordered.Length;
        LastSpeechDiagnostics = (coverage, decoy);
        // Calibrated on Chance S01 with Silero VAD and diarization: the right timing puts 38-55 %
        // of cues on an onset, 3.0-3.8x the shifted decoys; a poorly timed episode reached 1.7x
        // and subtitles of another episode found no alignment at all.
        var decision = coverage >= 0.3 && coverage >= decoy * 2.6 && best.Segments.Count <= MaximumSafeSegments
            ? SubtitleSyncDecision.SafeToSynchronize
            : coverage >= 0.2 && coverage >= decoy * 1.8
                ? SubtitleSyncDecision.NeedsReview
                : SubtitleSyncDecision.Rejected;

        return new PiecewiseSubtitleSync(decision, best.Segments, coverage, TimeSpan.Zero, best.Dropped, best.Cues);
    }

    /// <summary>Matched and decoy onset rates of the last speech fit, for calibration logs.</summary>
    public (double Matched, double Decoy) LastSpeechDiagnostics { get; private set; }

    private static SpeechEvaluation EvaluateSpeech(long[] onsets, SubtitleCue[] candidate, double scale)
    {
        var starts = candidate.Select(cue => ScaleTicks(cue.Start.Ticks, scale)).ToArray();
        var ends = candidate.Select(cue => ScaleTicks(cue.End.Ticks, scale)).ToArray();

        var peaks = FindOnsetPeaks(onsets, starts);
        if (peaks.Count == 0)
            return SpeechEvaluation.Empty;

        for (var peak = 0; peak < peaks.Count; peak++)
            peaks[peak] = RefineOnsetOffset(onsets, starts, peaks[peak]);

        var matched = new bool[candidate.Length, peaks.Count];
        for (var position = 0; position < candidate.Length; position++)
        for (var peak = 0; peak < peaks.Count; peak++)
            matched[position, peak] = OnsetResidual(onsets, starts[position] + peaks[peak]) <= SpeechOnsetToleranceTicks;

        var states = AssignSegments(matched, candidate.Length, peaks.Count);
        AbsorbWeakSegments(states, matched);

        var segments = new List<SubtitleSyncSegment>();
        var cues = new List<SubtitleCue>();
        var matchedTotal = 0;
        double decoyTotal = 0;
        var dropped = 0;
        long previousStart = -1, previousEnd = -1;

        var runStart = 0;
        while (runStart < candidate.Length)
        {
            var state = states[runStart];
            var runEnd = runStart;
            while (runEnd + 1 < candidate.Length && states[runEnd + 1] == state)
                runEnd++;

            var offset = peaks[state];
            var runMatched = 0;
            for (var position = runStart; position <= runEnd; position++)
            {
                var start = starts[position] + offset;
                var end = ends[position] + offset;
                if (OnsetResidual(onsets, start) <= SpeechOnsetToleranceTicks)
                    runMatched++;
                decoyTotal += SpeechDecoyShifts.Count(shift => OnsetResidual(onsets, start + shift.Ticks) <= SpeechOnsetToleranceTicks)
                              / (double)SpeechDecoyShifts.Length;

                if (start < 0 || start < previousStart || (position == runStart && start < previousEnd - PeakSuppressionTicks / 2))
                {
                    dropped++;
                    continue;
                }
                cues.Add(new SubtitleCue(cues.Count + 1, TimeSpan.FromTicks(start), TimeSpan.FromTicks(end), candidate[position].Text));
                previousStart = start;
                previousEnd = end;
            }

            matchedTotal += runMatched;
            segments.Add(new SubtitleSyncSegment(candidate[runStart].Start, new SubtitleTimeTransform(scale, TimeSpan.FromTicks(offset)), runMatched));
            runStart = runEnd + 1;
        }

        return new SpeechEvaluation(segments, matchedTotal, decoyTotal, dropped, cues);
    }

    /// <summary>Every cue start votes for the offsets that would put it on a speech onset.</summary>
    private static List<long> FindOnsetPeaks(long[] onsets, long[] starts)
    {
        var votes = new Dictionary<long, int>();
        foreach (var start in starts)
        {
            var first = LowerBound(onsets, start - MaximumOffsetTicks);
            for (var index = first; index < onsets.Length && onsets[index] <= start + MaximumOffsetTicks; index++)
            {
                var bin = (long)Math.Round((onsets[index] - start) / (double)OffsetBinTicks);
                votes[bin] = votes.GetValueOrDefault(bin) + 1;
            }
        }
        if (votes.Count == 0)
            return [];

        var scored = votes.Keys
            .Select(bin => (Bin: bin, Score: votes.GetValueOrDefault(bin - 1) + votes[bin] + votes.GetValueOrDefault(bin + 1)))
            .ToArray();
        // Random alignments still collect votes; a real stretch stands well above that floor.
        var background = scored.Select(item => (double)item.Score).Order().ElementAt(scored.Length / 2);
        var minimum = Math.Max(MinimumPeakVotes, background * 2);

        var peaks = new List<long>();
        var suppressionBins = PeakSuppressionTicks / OffsetBinTicks;
        foreach (var (bin, score) in scored.OrderByDescending(item => item.Score).ThenBy(item => Math.Abs(item.Bin)))
        {
            if (score < minimum || peaks.Count == MaximumOffsetPeaks)
                break;
            if (peaks.Any(peak => Math.Abs(peak / OffsetBinTicks - bin) <= suppressionBins))
                continue;
            peaks.Add(bin * OffsetBinTicks);
        }
        return peaks;
    }

    private static long RefineOnsetOffset(long[] onsets, long[] starts, long offset)
    {
        var deltas = new List<double>();
        foreach (var start in starts)
        {
            var target = start + offset;
            var nearest = Nearest(onsets, target);
            if (Math.Abs(nearest - target) <= SpeechOnsetToleranceTicks)
                deltas.Add(nearest - start);
        }
        return deltas.Count < MinimumMatchedCues ? offset : (long)Math.Round(Median(deltas.ToArray()));
    }

    private static long OnsetResidual(long[] onsets, long target) => Math.Abs(Nearest(onsets, target) - target);

    private static int LowerBound(long[] values, long target)
    {
        var position = Array.BinarySearch(values, target);
        if (position < 0)
            return ~position;
        while (position > 0 && values[position - 1] == target)
            position--;
        return position;
    }

    private sealed record SpeechEvaluation(
        IReadOnlyList<SubtitleSyncSegment> Segments,
        int Matched,
        double DecoyMatched,
        int Dropped,
        IReadOnlyList<SubtitleCue> Cues)
    {
        public static SpeechEvaluation Empty { get; } = new([], 0, 0, 0, []);
    }
}
