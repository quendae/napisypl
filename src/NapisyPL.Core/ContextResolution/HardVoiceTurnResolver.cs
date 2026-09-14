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
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int currentCueId)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(cueSpeakers);
        ArgumentNullException.ThrowIfNull(cueGenderEvidence);

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
            "next_cue_opposite_gender",
            currentSpeaker,
            nextSpeaker,
            currentGender,
            nextGender,
            confidence);
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

        if (evidence.Gender != SpeakerVoiceGender.Unknown)
        {
            gender = evidence.Gender;
            confidence = evidence.Confidence;
            return true;
        }

        // Experimental hard mode deliberately ignores the normal evidence gates.
        // If the audio classifier got any usable directional signal, force it to
        // Male or Female so we can measure how a pure binary voice strategy behaves.
        if (evidence.DirectionalGender != SpeakerVoiceGender.Unknown &&
            evidence.DirectionalConfidence > 0 &&
            evidence.CombinedEvidence > 0)
        {
            gender = evidence.DirectionalGender;
            confidence = evidence.DirectionalConfidence;
            return true;
        }

        return false;
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
