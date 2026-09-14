using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed partial class DeterministicGenderReviewService
{
    private static readonly IReadOnlyDictionary<string, string> FirstPersonPredicateMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nieśmiały"] = "nieśmiała",
            ["gotowy"] = "gotowa",
            ["pewny"] = "pewna",
            ["szczęśliwy"] = "szczęśliwa",
            ["zadowolony"] = "zadowolona",
            ["zmęczony"] = "zmęczona",
            ["spóźniony"] = "spóźniona",
            ["sam"] = "sama"
        };

    private static readonly IReadOnlyDictionary<string, string> FirstPersonPredicateFemaleToMale =
        FirstPersonPredicateMaleToFemale.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    private static bool TryGetHardVoiceSpeakerSelfGenderSafely(
        string? currentSpeaker,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId,
        string translatedText,
        out SpeakerVoiceGender gender,
        out double confidence)
    {
        gender = SpeakerVoiceGender.Unknown;
        confidence = 0;

        // A stable diarized speaker classification is stronger than morphology
        // already present in a machine translation, so it may normalize the cue.
        if (!string.IsNullOrWhiteSpace(currentSpeaker) &&
            speakerGenderEvidence.TryGetValue(currentSpeaker!, out var speakerEvidence) &&
            SpeakerGenderReviewEligibility.IsEligible(speakerEvidence))
        {
            gender = speakerEvidence.Gender;
            confidence = speakerEvidence.Confidence;
            return true;
        }

        if (!cueGenderEvidence.TryGetValue(cueId, out var cueEvidence) ||
            cueEvidence.Gender == SpeakerVoiceGender.Unknown)
        {
            return false;
        }

        // A single cue classifier can be wrong. If the translated sentence itself
        // contains two independent, consistent first-person markers, do not flip it
        // solely because one local audio sample disagrees.
        var strongTextGender = InferStrongSelfGender(translatedText);
        if (strongTextGender != SpeakerVoiceGender.Unknown && strongTextGender != cueEvidence.Gender)
            return false;

        gender = cueEvidence.Gender;
        confidence = cueEvidence.Confidence;
        return true;
    }

    private static bool ShouldPreserveStrongAddresseeGender(
        string translatedText,
        SpeakerVoiceGender proposedGender)
    {
        var strongTextGender = InferStrongAddresseeGender(translatedText);
        return strongTextGender != SpeakerVoiceGender.Unknown && strongTextGender != proposedGender;
    }

    private static SpeakerVoiceGender InferStrongSelfGender(string text)
    {
        var words = WordRegex().Matches(text).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var male = 0;
        var female = 0;

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (word.EndsWith("łabym", StringComparison.Ordinal) && word.Length > 5)
                female++;
            else if (word.EndsWith("łbym", StringComparison.Ordinal) && word.Length > 4)
                male++;

            if (word == "gdybym" && TryFindConditionalParticiple(words, index + 1, out var contextualGender))
            {
                if (contextualGender == SpeakerVoiceGender.Male) male++;
                if (contextualGender == SpeakerVoiceGender.Female) female++;
            }
        }

        return StrongGender(male, female);
    }

    private static SpeakerVoiceGender InferStrongAddresseeGender(string text)
    {
        var words = WordRegex().Matches(text).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var male = 0;
        var female = 0;

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (word.EndsWith("łabyś", StringComparison.Ordinal) && word.Length > 5)
                female++;
            else if (word.EndsWith("łbyś", StringComparison.Ordinal) && word.Length > 4)
                male++;
            else if (word.EndsWith("łaś", StringComparison.Ordinal) && word.Length > 3)
                female++;
            else if (word.EndsWith("łeś", StringComparison.Ordinal) && word.Length > 3)
                male++;

            if (word == "gdybyś" && TryFindConditionalParticiple(words, index + 1, out var contextualGender))
            {
                if (contextualGender == SpeakerVoiceGender.Male) male++;
                if (contextualGender == SpeakerVoiceGender.Female) female++;
            }
        }

        return StrongGender(male, female);
    }

    private static bool TryFindConditionalParticiple(
        IReadOnlyList<string> words,
        int startIndex,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        var end = Math.Min(words.Count, startIndex + 4);
        for (var index = startIndex; index < end; index++)
        {
            var word = words[index];
            if (word.Length > 2 && word.EndsWith("ła", StringComparison.Ordinal))
            {
                gender = SpeakerVoiceGender.Female;
                return true;
            }

            if (word.Length > 1 && word.EndsWith("ł", StringComparison.Ordinal))
            {
                gender = SpeakerVoiceGender.Male;
                return true;
            }
        }

        return false;
    }

    private static SpeakerVoiceGender StrongGender(int maleMarkers, int femaleMarkers)
    {
        if (maleMarkers >= 2 && femaleMarkers == 0)
            return SpeakerVoiceGender.Male;
        if (femaleMarkers >= 2 && maleMarkers == 0)
            return SpeakerVoiceGender.Female;
        return SpeakerVoiceGender.Unknown;
    }

    private static string FixFirstPersonPredicateAgreement(string text, SpeakerVoiceGender gender)
    {
        if (gender == SpeakerVoiceGender.Unknown)
            return text;

        var map = gender == SpeakerVoiceGender.Female
            ? FirstPersonPredicateMaleToFemale
            : FirstPersonPredicateFemaleToMale;

        return FirstPersonPredicateRegex().Replace(text, match =>
        {
            var original = match.Groups["predicate"].Value;
            var lower = original.ToLowerInvariant();
            if (!map.TryGetValue(lower, out var replacement))
                return match.Value;

            var adjusted = MatchCasing(original, replacement);
            return match.Value[..match.Groups["predicate"].Index + adjusted.Length - match.Index - adjusted.Length]
                   + adjusted
                   + match.Value[(match.Groups["predicate"].Index - match.Index + original.Length)..];
        });
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"\bjestem\s+(?<predicate>nieśmiały|nieśmiała|gotowy|gotowa|pewny|pewna|szczęśliwy|szczęśliwa|zadowolony|zadowolona|zmęczony|zmęczona|spóźniony|spóźniona|sam|sama)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex FirstPersonPredicateRegex();
}
