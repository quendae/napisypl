using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed partial class DeterministicGenderReviewService
{
    private const double MinimumAddresseeConfidence = 0.94;

    private static readonly IReadOnlyDictionary<string, string> SpeakerMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mógłbym"] = "mogłabym",
            ["poszedłem"] = "poszłam",
            ["wyszedłem"] = "wyszłam",
            ["przyszedłem"] = "przyszłam",
            ["odszedłem"] = "odeszłam"
        };

    private static readonly IReadOnlyDictionary<string, string> AddresseeMaleToFemale =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["mógłbyś"] = "mogłabyś",
            ["poszedłeś"] = "poszłaś",
            ["wyszedłeś"] = "wyszłaś",
            ["przyszedłeś"] = "przyszłaś",
            ["odszedłeś"] = "odeszłaś"
        };

    private static readonly IReadOnlyDictionary<string, string> SpeakerFemaleToMale = Reverse(SpeakerMaleToFemale);
    private static readonly IReadOnlyDictionary<string, string> AddresseeFemaleToMale = Reverse(AddresseeMaleToFemale);

    public IReadOnlyList<SubtitleCue> Review(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence>? cueGenderEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(translated);
        ArgumentNullException.ThrowIfNull(cueSpeakers);
        ArgumentNullException.ThrowIfNull(speakerGenderEvidence);

        if (source.Count == 0 || translated.Count == 0)
            return translated.ToArray();

        var sourceIds = source.Select(cue => cue.Index).ToHashSet();
        var result = new List<SubtitleCue>(translated.Count);
        var localCueGender = cueGenderEvidence ?? new Dictionary<int, CueVoiceGenderEvidence>();

        foreach (var cue in translated)
        {
            if (!sourceIds.Contains(cue.Index))
            {
                result.Add(cue);
                continue;
            }

            cueSpeakers.TryGetValue(cue.Index, out var currentSpeaker);
            var text = cue.Text;

            if (!string.IsNullOrWhiteSpace(currentSpeaker) &&
                TryEligibleGender(currentSpeaker!, speakerGenderEvidence, out var speakerGender))
            {
                text = FixSpeakerAgreement(text, speakerGender);
            }

            var localTurn = LocalTurnGenderResolver.Resolve(source, cueSpeakers, localCueGender, cue.Index);
            if (localTurn.IsResolved && localTurn.Confidence >= MinimumAddresseeConfidence)
            {
                text = FixAddresseeAgreement(text, localTurn.Gender);
            }
            else if (!string.IsNullOrWhiteSpace(currentSpeaker))
            {
                var addressee = DialogueAddresseeResolver.ResolveDetailed(source, cueSpeakers, cue.Index);
                if (addressee.IsResolved &&
                    addressee.Confidence >= MinimumAddresseeConfidence &&
                    !string.Equals(addressee.SpeakerId, currentSpeaker, StringComparison.Ordinal) &&
                    TryEligibleGender(addressee.SpeakerId!, speakerGenderEvidence, out var addresseeGender))
                {
                    text = FixAddresseeAgreement(text, addresseeGender);
                }
            }

            result.Add(string.Equals(text, cue.Text, StringComparison.Ordinal)
                ? cue
                : cue with { Text = text });
        }

        return result;
    }

    internal static string FixSpeakerAgreement(string text, SpeakerVoiceGender gender) =>
        gender switch
        {
            SpeakerVoiceGender.Female => FixWords(text, SpeakerMaleToFemale, [("łbym", "łabym"), ("łem", "łam")]),
            SpeakerVoiceGender.Male => FixWords(text, SpeakerFemaleToMale, [("łabym", "łbym"), ("łam", "łem")]),
            _ => text
        };

    internal static string FixAddresseeAgreement(string text, SpeakerVoiceGender gender) =>
        gender switch
        {
            SpeakerVoiceGender.Female => FixWords(text, AddresseeMaleToFemale, [("łbyś", "łabyś"), ("łeś", "łaś")]),
            SpeakerVoiceGender.Male => FixWords(text, AddresseeFemaleToMale, [("łabyś", "łbyś"), ("łaś", "łeś")]),
            _ => text
        };

    private static bool TryEligibleGender(
        string speakerId,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> evidence,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        if (!evidence.TryGetValue(speakerId, out var value) || !SpeakerGenderReviewEligibility.IsEligible(value))
            return false;
        gender = value.Gender;
        return true;
    }

    private static string FixWords(
        string text,
        IReadOnlyDictionary<string, string> irregular,
        IReadOnlyList<(string From, string To)> suffixes) =>
        WordRegex().Replace(text, match =>
        {
            var original = match.Value;
            var lower = original.ToLowerInvariant();

            if (irregular.TryGetValue(lower, out var irregularReplacement))
                return MatchCasing(original, irregularReplacement);

            foreach (var (from, to) in suffixes)
            {
                if (!lower.EndsWith(from, StringComparison.Ordinal) || lower.Length <= from.Length)
                    continue;
                var replacement = lower[..^from.Length] + to;
                return MatchCasing(original, replacement);
            }

            return original;
        });

    private static string MatchCasing(string source, string replacement)
    {
        if (source.All(character => !char.IsLetter(character) || char.IsUpper(character)))
            return replacement.ToUpperInvariant();

        if (source.Length > 0 && char.IsUpper(source[0]))
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];

        return replacement;
    }

    private static IReadOnlyDictionary<string, string> Reverse(IReadOnlyDictionary<string, string> source) =>
        source.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}
