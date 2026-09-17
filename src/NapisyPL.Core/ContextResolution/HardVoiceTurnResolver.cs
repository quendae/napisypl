using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record HardVoiceTurnResolution(
    bool IsResolved,
    string ReasonCode,
    string? CurrentSpeakerId,
    string? TargetSpeakerId,
    SpeakerVoiceGender CurrentGender,
    SpeakerVoiceGender TargetGender,
    double Confidence);

public static class HardVoiceTurnResolver
{
    private const double MinimumCueGenderConfidence = 0.82;
    private const double MinimumCueCombinedEvidence = 0.03;
    private const double MinimumCueDurationSeconds = 0.75;
    private static readonly TimeSpan MaximumTurnGap = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan MaximumOverlap = TimeSpan.FromMilliseconds(350);

    /// <summary>
    /// Two cues whose measured pitch is confidently male and confidently female
    /// cannot be one person, whatever label diarization gave them. On film audio
    /// the clustering both fragments and merges speakers, so above this bar the
    /// pitch wins over the label.
    /// </summary>
    public const double PitchOverridesDiarizationConfidence = 0.90;

    public static HardVoiceTurnResolution Resolve(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int currentCueId)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(cueSpeakers);
        ArgumentNullException.ThrowIfNull(cueGenderEvidence);

        var position = CuePositionIndex.Find(cues, currentCueId);
        if (position < 0)
            return Unresolved("current_cue_missing");
        if (position + 1 >= cues.Count)
            return Unresolved("no_next_cue");

        var nextCue = cues[position + 1];
        var currentCue = cues[position];
        var gap = nextCue.Start >= currentCue.End ? nextCue.Start - currentCue.End : TimeSpan.Zero;
        var overlap = currentCue.End > nextCue.Start ? currentCue.End - nextCue.Start : TimeSpan.Zero;
        if (gap > MaximumTurnGap)
            return Unresolved("next_cue_too_far");
        if (overlap > MaximumOverlap)
            return Unresolved("next_cue_overlap");

        cueSpeakers.TryGetValue(currentCueId, out var currentSpeaker);
        cueSpeakers.TryGetValue(nextCue.Index, out var nextSpeaker);
        var sameDiarizedSpeaker =
            !string.IsNullOrWhiteSpace(currentSpeaker) &&
            !string.IsNullOrWhiteSpace(nextSpeaker) &&
            string.Equals(currentSpeaker, nextSpeaker, StringComparison.Ordinal);

        // A speaker often spreads one line over several cues ("Did you or did you
        // not make love to him? / Were you not lovers with Bill Hayden?") and the
        // addressee answers only after the last one. Follow such a continuation to
        // the first cue that is actually someone else.
        var targetPosition = position + 1;
        var continuationHops = 0;
        while (sameDiarizedSpeaker && !PitchProvesDifferentSpeakers(cueGenderEvidence, currentCueId, nextCue.Index))
        {
            if (continuationHops >= MaximumContinuationHops ||
                ContradictsCurrentPitch(cueGenderEvidence, currentCueId, nextCue.Index) ||
                targetPosition + 1 >= cues.Count)
            {
                return Unresolved("same_speaker_turn", currentSpeaker, nextSpeaker);
            }

            var following = cues[targetPosition + 1];
            var continuationGap = following.Start >= nextCue.End ? following.Start - nextCue.End : TimeSpan.Zero;
            var continuationOverlap = nextCue.End > following.Start ? nextCue.End - following.Start : TimeSpan.Zero;
            if (continuationGap > MaximumTurnGap || continuationOverlap > MaximumOverlap)
                return Unresolved("same_speaker_turn", currentSpeaker, nextSpeaker);

            targetPosition++;
            continuationHops++;
            nextCue = following;
            cueSpeakers.TryGetValue(nextCue.Index, out nextSpeaker);
            sameDiarizedSpeaker =
                !string.IsNullOrWhiteSpace(currentSpeaker) &&
                !string.IsNullOrWhiteSpace(nextSpeaker) &&
                string.Equals(currentSpeaker, nextSpeaker, StringComparison.Ordinal);
        }

        if (HasImmediateThirdSpeaker(cues, cueSpeakers, targetPosition - 1, currentSpeaker, nextSpeaker))
            return Unresolved("third_speaker_ambiguity", currentSpeaker, nextSpeaker);

        if (!TryGetForcedGender(cueGenderEvidence, currentCueId, out var currentGender, out var currentConfidence))
        {
            return Unresolved(
                "current_gender_unknown",
                currentSpeaker,
                nextSpeaker);
        }

        if (!TryGetForcedGender(cueGenderEvidence, nextCue.Index, out var nextGender, out var nextConfidence))
        {
            return new HardVoiceTurnResolution(
                false,
                "next_gender_unknown",
                currentSpeaker,
                nextSpeaker,
                currentGender,
                SpeakerVoiceGender.Unknown,
                currentConfidence);
        }

        var confidence = Math.Min(currentConfidence, nextConfidence);
        if (currentGender == nextGender)
        {
            return new HardVoiceTurnResolution(
                false,
                "same_gender_turn",
                currentSpeaker,
                nextSpeaker,
                currentGender,
                nextGender,
                confidence);
        }

        return new HardVoiceTurnResolution(
            true,
            continuationHops > 0
                ? "reply_after_speaker_continuation"
                : sameDiarizedSpeaker ? "next_cue_opposite_pitch_overrides_diarization" : "next_cue_opposite_gender",
            currentSpeaker,
            nextSpeaker,
            currentGender,
            nextGender,
            confidence);
    }

    /// <summary>
    /// A-B-A: the same speaker talks right before and right after this cue, so this
    /// cue answers them and "you" means A. Unlike <see cref="Resolve"/> it does not
    /// need B's gender, which is what two women (or two men) talking needs
    /// (MPG S01E01 #15 "You are not old." between two lines of the same woman).
    /// </summary>
    public static HardVoiceTurnResolution ResolveAddresseeBetweenSameSpeaker(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        int currentCueId)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(cueSpeakers);
        ArgumentNullException.ThrowIfNull(cueGenderEvidence);
        ArgumentNullException.ThrowIfNull(speakerGenderEvidence);

        var position = CuePositionIndex.Find(cues, currentCueId);
        if (position <= 0 || position + 1 >= cues.Count)
            return Unresolved("no_surrounding_cues");

        var previous = cues[position - 1];
        var current = cues[position];
        var next = cues[position + 1];
        if (!IsTurnBoundary(previous, current) || !IsTurnBoundary(current, next))
            return Unresolved("surrounding_cues_too_far");

        cueSpeakers.TryGetValue(currentCueId, out var currentSpeaker);
        cueSpeakers.TryGetValue(previous.Index, out var previousSpeaker);
        cueSpeakers.TryGetValue(next.Index, out var nextSpeaker);
        if (string.IsNullOrWhiteSpace(currentSpeaker) ||
            string.IsNullOrWhiteSpace(previousSpeaker) ||
            !string.Equals(previousSpeaker, nextSpeaker, StringComparison.Ordinal) ||
            string.Equals(previousSpeaker, currentSpeaker, StringComparison.Ordinal))
        {
            return Unresolved("not_between_same_speaker", currentSpeaker, nextSpeaker);
        }

        var hasPrevious = TryGetForcedGender(cueGenderEvidence, previous.Index, out var previousGender, out var previousConfidence);
        var hasNext = TryGetForcedGender(cueGenderEvidence, next.Index, out var nextGender, out var nextConfidence);
        if (hasPrevious && hasNext && previousGender != nextGender)
            return Unresolved("surrounding_speaker_gender_conflict", currentSpeaker, nextSpeaker);

        var gender = SpeakerVoiceGender.Unknown;
        var confidence = 0d;
        if (hasPrevious && hasNext)
        {
            gender = previousGender;
            confidence = Math.Min(previousConfidence, nextConfidence);
        }
        else if ((hasPrevious || hasNext) &&
                 speakerGenderEvidence.TryGetValue(previousSpeaker!, out var profile) &&
                 SpeakerGenderReviewEligibility.IsEligible(profile) &&
                 profile.Gender == (hasPrevious ? previousGender : nextGender))
        {
            gender = profile.Gender;
            confidence = Math.Min(profile.Confidence, hasPrevious ? previousConfidence : nextConfidence);
        }

        if (gender == SpeakerVoiceGender.Unknown)
            return Unresolved("surrounding_speaker_gender_unknown", currentSpeaker, nextSpeaker);

        return new HardVoiceTurnResolution(
            true,
            "addressee_between_same_speaker",
            currentSpeaker,
            nextSpeaker,
            SpeakerVoiceGender.Unknown,
            gender,
            confidence);
    }

    private static bool IsTurnBoundary(SubtitleCue first, SubtitleCue second)
    {
        var gap = second.Start >= first.End ? second.Start - first.End : TimeSpan.Zero;
        var overlap = first.End > second.Start ? first.End - second.Start : TimeSpan.Zero;
        return gap <= MaximumTurnGap && overlap <= MaximumOverlap;
    }

    public static bool TryGetForcedGender(
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId,
        out SpeakerVoiceGender gender,
        out double confidence)
    {
        gender = SpeakerVoiceGender.Unknown;
        confidence = 0;

        if (!cueGenderEvidence.TryGetValue(cueId, out var evidence))
            return false;

        if (evidence.Gender != SpeakerVoiceGender.Unknown &&
            evidence.Confidence >= MinimumCueGenderConfidence &&
            evidence.CombinedEvidence >= MinimumCueCombinedEvidence &&
            evidence.DurationSeconds >= MinimumCueDurationSeconds)
        {
            gender = evidence.Gender;
            confidence = evidence.Confidence;
            return true;
        }

        return false;
    }

    /// <summary>How many same-speaker continuation cues may be skipped to find the reply.</summary>
    private const int MaximumContinuationHops = 2;

    private static bool ContradictsCurrentPitch(
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int currentCueId,
        int candidateCueId) =>
        TryGetForcedGender(cueGenderEvidence, currentCueId, out var currentGender, out _) &&
        TryGetForcedGender(cueGenderEvidence, candidateCueId, out var candidateGender, out _) &&
        currentGender != candidateGender;

    private static bool PitchProvesDifferentSpeakers(
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int currentCueId,
        int nextCueId) =>
        TryGetForcedGender(cueGenderEvidence, currentCueId, out var currentGender, out var currentConfidence) &&
        TryGetForcedGender(cueGenderEvidence, nextCueId, out var nextGender, out var nextConfidence) &&
        currentGender != nextGender &&
        Math.Min(currentConfidence, nextConfidence) >= PitchOverridesDiarizationConfidence;

    private static bool HasImmediateThirdSpeaker(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int currentPosition,
        string? currentSpeaker,
        string? nextSpeaker)
    {
        if (string.IsNullOrWhiteSpace(currentSpeaker) ||
            string.IsNullOrWhiteSpace(nextSpeaker) ||
            currentPosition + 2 >= cues.Count)
        {
            return false;
        }

        var next = cues[currentPosition + 1];
        var after = cues[currentPosition + 2];
        var gap = after.Start >= next.End ? after.Start - next.End : TimeSpan.Zero;
        return gap <= MaximumTurnGap &&
               cueSpeakers.TryGetValue(after.Index, out var afterSpeaker) &&
               !string.IsNullOrWhiteSpace(afterSpeaker) &&
               !string.Equals(afterSpeaker, currentSpeaker, StringComparison.Ordinal) &&
               !string.Equals(afterSpeaker, nextSpeaker, StringComparison.Ordinal);
    }

    private static HardVoiceTurnResolution Unresolved(
        string reasonCode,
        string? currentSpeakerId = null,
        string? targetSpeakerId = null) =>
        new(
            false,
            reasonCode,
            currentSpeakerId,
            targetSpeakerId,
            SpeakerVoiceGender.Unknown,
            SpeakerVoiceGender.Unknown,
            0);
}
