using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record CueVoiceGenderEvidence(
    SpeakerVoiceGender Gender,
    double Confidence,
    double CombinedEvidence,
    double DurationSeconds);

public static class CueGenderEvidenceEvaluator
{
    private const double MinimumDurationSeconds = 0.75;
    private const double MinimumCombinedEvidence = 0.03;
    private const double MinimumNormalizedConfidence = 0.82;

    public static CueVoiceGenderEvidence Evaluate(double male, double female, double durationSeconds)
    {
        if (!double.IsFinite(male) || !double.IsFinite(female) || !double.IsFinite(durationSeconds) ||
            male < 0 || female < 0 || male > 1 || female > 1 || durationSeconds < MinimumDurationSeconds)
        {
            return new CueVoiceGenderEvidence(SpeakerVoiceGender.Unknown, 0, 0, Math.Max(0, durationSeconds));
        }

        var combined = male + female;
        if (combined < MinimumCombinedEvidence)
            return new CueVoiceGenderEvidence(SpeakerVoiceGender.Unknown, 0, combined, durationSeconds);

        var confidence = Math.Max(male, female) / combined;
        if (confidence < MinimumNormalizedConfidence)
            return new CueVoiceGenderEvidence(SpeakerVoiceGender.Unknown, confidence, combined, durationSeconds);

        return new CueVoiceGenderEvidence(
            male >= female ? SpeakerVoiceGender.Male : SpeakerVoiceGender.Female,
            confidence,
            combined,
            durationSeconds);
    }
}

public sealed record LocalTurnGenderResolution(
    SpeakerVoiceGender Gender,
    double Confidence,
    string ReasonCode)
{
    public bool IsResolved => Gender != SpeakerVoiceGender.Unknown;

    public static LocalTurnGenderResolution Unresolved(string reasonCode) =>
        new(SpeakerVoiceGender.Unknown, 0, reasonCode);
}

public static class LocalTurnGenderResolver
{
    private static readonly TimeSpan MaximumTurnGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ImmediateQuestionGap = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan MaximumOverlap = TimeSpan.FromMilliseconds(350);

    public static LocalTurnGenderResolution Resolve(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId)
    {
        var position = FindPosition(cues, cueId);
        if (position < 0)
            return LocalTurnGenderResolution.Unresolved("missing_cue");
        if (position + 1 >= cues.Count)
            return LocalTurnGenderResolution.Unresolved("missing_next_cue");

        var current = cues[position];
        var next = cues[position + 1];
        var gap = next.Start >= current.End ? next.Start - current.End : TimeSpan.Zero;
        var overlap = current.End > next.Start ? current.End - next.Start : TimeSpan.Zero;
        if (gap > MaximumTurnGap)
            return LocalTurnGenderResolution.Unresolved("next_cue_too_far");
        if (overlap > MaximumOverlap)
            return LocalTurnGenderResolution.Unresolved("next_cue_overlap");

        if (!TryEligible(cueGenderEvidence, current.Index, out var currentGender) ||
            !TryEligible(cueGenderEvidence, next.Index, out var nextGender))
        {
            return LocalTurnGenderResolution.Unresolved("missing_local_gender");
        }

        if (currentGender.Gender != nextGender.Gender)
        {
            return new LocalTurnGenderResolution(
                nextGender.Gender,
                Math.Min(currentGender.Confidence, nextGender.Confidence),
                "next_cue_gender_change");
        }

        if (!cueSpeakers.TryGetValue(current.Index, out var currentSpeaker) ||
            string.IsNullOrWhiteSpace(currentSpeaker) ||
            !cueSpeakers.TryGetValue(next.Index, out var nextSpeaker) ||
            string.IsNullOrWhiteSpace(nextSpeaker) ||
            string.Equals(currentSpeaker, nextSpeaker, StringComparison.Ordinal))
        {
            return LocalTurnGenderResolution.Unresolved("same_gender_same_or_missing_speaker");
        }

        // A short question immediately answered by a different local speaker is
        // strong turn-taking evidence even when global diarization fragmented that
        // character into many SPEAKER_x IDs. This is the primary path for cases
        // such as "You got married?" -> immediate reply from the other man.
        if (gap <= ImmediateQuestionGap && current.Text.Contains('?', StringComparison.Ordinal))
        {
            return new LocalTurnGenderResolution(
                nextGender.Gender,
                Math.Min(0.96, Math.Min(currentGender.Confidence, nextGender.Confidence)),
                "next_cue_same_gender_question");
        }

        var stableTurn = IsStableSameGenderNextTurn(cues, cueSpeakers, position, nextSpeaker!);
        if (!stableTurn)
            return LocalTurnGenderResolution.Unresolved("unstable_same_gender_turn");

        return new LocalTurnGenderResolution(
            nextGender.Gender,
            Math.Min(0.95, Math.Min(currentGender.Confidence, nextGender.Confidence)),
            "next_cue_same_gender_stable_turn");
    }

    private static bool IsStableSameGenderNextTurn(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int currentPosition,
        string nextSpeaker)
    {
        if (currentPosition + 2 >= cues.Count)
            return false;

        var next = cues[currentPosition + 1];
        var after = cues[currentPosition + 2];
        var gap = after.Start >= next.End ? after.Start - next.End : TimeSpan.Zero;
        if (gap > MaximumTurnGap)
            return false;

        return cueSpeakers.TryGetValue(after.Index, out var afterSpeaker) &&
               string.Equals(afterSpeaker, nextSpeaker, StringComparison.Ordinal);
    }

    private static bool TryEligible(
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> evidence,
        int cueId,
        out CueVoiceGenderEvidence value)
    {
        value = default!;
        if (!evidence.TryGetValue(cueId, out var candidate) ||
            candidate.Gender == SpeakerVoiceGender.Unknown ||
            candidate.Confidence < 0.82 ||
            candidate.CombinedEvidence < 0.03 ||
            candidate.DurationSeconds < 0.75)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static int FindPosition(IReadOnlyList<SubtitleCue> cues, int cueId)
    {
        for (var i = 0; i < cues.Count; i++)
        {
            if (cues[i].Index == cueId)
                return i;
        }

        return -1;
    }
}
