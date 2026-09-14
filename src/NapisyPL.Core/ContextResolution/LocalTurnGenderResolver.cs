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
        int cueId,
        IReadOnlyDictionary<string, SpeakerGenderEvidence>? speakerGenderEvidence = null)
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

        cueSpeakers.TryGetValue(current.Index, out var currentSpeaker);
        cueSpeakers.TryGetValue(next.Index, out var nextSpeaker);
        var differentLocalSpeakers =
            !string.IsNullOrWhiteSpace(currentSpeaker) &&
            !string.IsNullOrWhiteSpace(nextSpeaker) &&
            !string.Equals(currentSpeaker, nextSpeaker, StringComparison.Ordinal);

        if (!TryResolveGender(
                next.Index,
                cueSpeakers,
                cueGenderEvidence,
                speakerGenderEvidence,
                out var nextGender,
                out var nextConfidence))
        {
            return LocalTurnGenderResolution.Unresolved("missing_next_gender");
        }

        var hasCurrentGender = TryResolveGender(
            current.Index,
            cueSpeakers,
            cueGenderEvidence,
            speakerGenderEvidence,
            out var currentGender,
            out var currentConfidence);

        if (hasCurrentGender && currentGender != nextGender)
        {
            return new LocalTurnGenderResolution(
                nextGender,
                Math.Min(currentConfidence, nextConfidence),
                "next_cue_gender_change");
        }

        if (!differentLocalSpeakers)
        {
            return LocalTurnGenderResolution.Unresolved(
                hasCurrentGender ? "same_gender_same_or_missing_speaker" : "missing_current_gender_and_speaker_change");
        }

        // A short question immediately answered by a different local speaker is
        // strong turn-taking evidence. Per-cue audio remains useful, but when it
        // agrees with stronger speaker-level evidence we keep the stronger
        // confidence instead of letting a noisy short cue downgrade the turn.
        if (gap <= ImmediateQuestionGap && current.Text.Contains('?'))
        {
            var confidence = hasCurrentGender
                ? Math.Min(currentConfidence, nextConfidence)
                : nextConfidence;
            return new LocalTurnGenderResolution(
                nextGender,
                Math.Min(0.96, confidence),
                "next_cue_same_gender_question");
        }

        var stableTurn = IsStableSameGenderNextTurn(cues, cueSpeakers, position, nextSpeaker!);
        if (!stableTurn)
            return LocalTurnGenderResolution.Unresolved("unstable_same_gender_turn");

        var stableConfidence = hasCurrentGender
            ? Math.Min(currentConfidence, nextConfidence)
            : nextConfidence;
        return new LocalTurnGenderResolution(
            nextGender,
            Math.Min(0.95, stableConfidence),
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

    private static bool TryResolveGender(
        int cueId,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        IReadOnlyDictionary<string, SpeakerGenderEvidence>? speakerGenderEvidence,
        out SpeakerVoiceGender gender,
        out double confidence)
    {
        var hasLocal = TryEligible(cueGenderEvidence, cueId, out var local);
        var hasSpeaker = TryEligibleSpeaker(
            cueId,
            cueSpeakers,
            speakerGenderEvidence,
            out var speaker);

        if (hasLocal && hasSpeaker)
        {
            // Conflicting classifiers are a safety stop. If they agree, use the
            // stronger confidence rather than allowing a noisy short cue to veto
            // a stable multi-sample speaker classification.
            if (local.Gender != speaker.Gender)
            {
                gender = SpeakerVoiceGender.Unknown;
                confidence = 0;
                return false;
            }

            gender = local.Gender;
            confidence = Math.Max(local.Confidence, speaker.Confidence);
            return true;
        }

        if (hasLocal)
        {
            gender = local.Gender;
            confidence = local.Confidence;
            return true;
        }

        if (hasSpeaker)
        {
            gender = speaker.Gender;
            confidence = speaker.Confidence;
            return true;
        }

        gender = SpeakerVoiceGender.Unknown;
        confidence = 0;
        return false;
    }

    private static bool TryEligibleSpeaker(
        int cueId,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence>? speakerGenderEvidence,
        out SpeakerGenderEvidence value)
    {
        value = default!;
        if (speakerGenderEvidence is null ||
            !cueSpeakers.TryGetValue(cueId, out var speakerId) ||
            string.IsNullOrWhiteSpace(speakerId) ||
            !speakerGenderEvidence.TryGetValue(speakerId!, out var candidate) ||
            !SpeakerGenderReviewEligibility.IsEligible(candidate))
        {
            return false;
        }

        value = candidate;
        return true;
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
