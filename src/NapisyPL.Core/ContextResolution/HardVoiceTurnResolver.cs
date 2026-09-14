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
    public static HardVoiceTurnResolution Resolve(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        int currentCueId)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(cueSpeakers);
        ArgumentNullException.ThrowIfNull(speakerGenderEvidence);

        var position = -1;
        for (var index = 0; index < cues.Count; index++)
        {
            if (cues[index].Index == currentCueId)
            {
                position = index;
                break;
            }
        }

        if (position < 0)
            return Unresolved("current_cue_missing");
        if (position + 1 >= cues.Count)
            return Unresolved("no_next_cue");

        var nextCue = cues[position + 1];
        cueSpeakers.TryGetValue(currentCueId, out var currentSpeaker);
        cueSpeakers.TryGetValue(nextCue.Index, out var nextSpeaker);
        if (string.IsNullOrWhiteSpace(currentSpeaker) || string.IsNullOrWhiteSpace(nextSpeaker))
            return Unresolved("speaker_missing", currentSpeaker, nextSpeaker);
        if (string.Equals(currentSpeaker, nextSpeaker, StringComparison.Ordinal))
            return Unresolved("same_speaker", currentSpeaker, nextSpeaker);

        if (!speakerGenderEvidence.TryGetValue(currentSpeaker!, out var currentEvidence) ||
            !speakerGenderEvidence.TryGetValue(nextSpeaker!, out var nextEvidence) ||
            currentEvidence.Gender == SpeakerVoiceGender.Unknown ||
            nextEvidence.Gender == SpeakerVoiceGender.Unknown)
        {
            return Unresolved("gender_unknown", currentSpeaker, nextSpeaker);
        }

        if (!RoundsTo1000(currentEvidence.Confidence) || !RoundsTo1000(nextEvidence.Confidence))
        {
            return new HardVoiceTurnResolution(
                false,
                "confidence_below_1000",
                currentSpeaker,
                nextSpeaker,
                currentEvidence.Gender,
                nextEvidence.Gender,
                Math.Min(currentEvidence.Confidence, nextEvidence.Confidence));
        }

        if (currentEvidence.Gender == nextEvidence.Gender)
        {
            return new HardVoiceTurnResolution(
                false,
                "same_gender_turn",
                currentSpeaker,
                nextSpeaker,
                currentEvidence.Gender,
                nextEvidence.Gender,
                Math.Min(currentEvidence.Confidence, nextEvidence.Confidence));
        }

        return new HardVoiceTurnResolution(
            true,
            "next_speaker_opposite_gender_1000",
            currentSpeaker,
            nextSpeaker,
            currentEvidence.Gender,
            nextEvidence.Gender,
            Math.Min(currentEvidence.Confidence, nextEvidence.Confidence));
    }

    private static bool RoundsTo1000(double confidence) =>
        (int)Math.Round(Math.Clamp(confidence, 0, 1) * 1000, MidpointRounding.AwayFromZero) == 1000;

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
