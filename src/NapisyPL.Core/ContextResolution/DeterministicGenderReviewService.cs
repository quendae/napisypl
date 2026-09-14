using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record DeterministicGenderCueDiagnostic(
    int CueId,
    string? CurrentSpeaker,
    bool HasGenderedCandidate,
    string? CandidateWord,
    string Resolver,
    string ReasonCode,
    SpeakerVoiceGender TargetGender,
    double Confidence,
    bool GatePassed,
    string? MatchedWord,
    string? Replacement,
    bool Changed);

public sealed partial class DeterministicGenderReviewService
{
    private const double MinimumAddresseeConfidence = 0.94;
    private const double MinimumCueGenderConfidence = 0.82;
    private const double MinimumCueCombinedEvidence = 0.03;
    private const double MinimumCueDurationSeconds = 0.75;

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

    private static readonly string[] GenderedSuffixes =
    [
        "łabym", "łabyś", "łbym", "łbyś", "łam", "łem", "łaś", "łeś"
    ];

    public IReadOnlyList<SubtitleCue> Review(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence>? cueGenderEvidence = null,
        ICollection<DeterministicGenderCueDiagnostic>? diagnostics = null)
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
            var originalText = cue.Text;
            var candidateWord = FindGenderedCandidate(originalText);
            var text = originalText;

            if (!string.IsNullOrWhiteSpace(currentSpeaker) &&
                TryEligibleGender(currentSpeaker!, speakerGenderEvidence, out var speakerGender))
            {
                text = FixSpeakerAgreement(text, speakerGender);
            }

            var resolver = "none";
            var reasonCode = "unresolved";
            var targetGender = SpeakerVoiceGender.Unknown;
            var confidence = 0d;
            var gatePassed = false;

            var localTurn = LocalTurnGenderResolver.Resolve(
                source,
                cueSpeakers,
                localCueGender,
                cue.Index,
                speakerGenderEvidence);
            if (localTurn.IsResolved)
            {
                resolver = "local_turn";
                reasonCode = localTurn.ReasonCode;
                targetGender = localTurn.Gender;
                confidence = localTurn.Confidence;
                gatePassed = localTurn.Confidence >= MinimumAddresseeConfidence;
            }
            else
            {
                reasonCode = localTurn.ReasonCode;
            }

            if (localTurn.IsResolved && localTurn.Confidence >= MinimumAddresseeConfidence)
            {
                text = FixAddresseeAgreement(text, localTurn.Gender);
            }
            else if (!string.IsNullOrWhiteSpace(currentSpeaker))
            {
                var addressee = DialogueAddresseeResolver.ResolveDetailed(source, cueSpeakers, cue.Index);
                if (addressee.IsResolved)
                {
                    resolver = "dialogue_addressee";
                    reasonCode = addressee.ReasonCode;
                    confidence = addressee.Confidence;
                    gatePassed = addressee.Confidence >= MinimumAddresseeConfidence;
                }

                if (addressee.IsResolved &&
                    addressee.Confidence >= MinimumAddresseeConfidence &&
                    !string.Equals(addressee.SpeakerId, currentSpeaker, StringComparison.Ordinal) &&
                    TryEligibleGender(addressee.SpeakerId!, speakerGenderEvidence, out var addresseeGender))
                {
                    if (TryGetImmediateAddresseeCueGender(
                            source,
                            cueSpeakers,
                            localCueGender,
                            cue.Index,
                            addressee.SpeakerId!,
                            out var localAddresseeGender) &&
                        localAddresseeGender != addresseeGender)
                    {
                        reasonCode = "addressee_gender_conflict";
                        targetGender = SpeakerVoiceGender.Unknown;
                        gatePassed = false;
                    }
                    else
                    {
                        targetGender = addresseeGender;
                        text = FixAddresseeAgreement(text, addresseeGender);
                    }
                }
            }

            var changed = !string.Equals(text, originalText, StringComparison.Ordinal);
            var (matchedWord, replacement) = changed
                ? FindFirstChangedWordPair(originalText, text)
                : (null, null);

            if (diagnostics is not null && (candidateWord is not null || changed))
            {
                diagnostics.Add(new DeterministicGenderCueDiagnostic(
                    cue.Index,
                    currentSpeaker,
                    candidateWord is not null,
                    candidateWord,
                    resolver,
                    reasonCode,
                    targetGender,
                    confidence,
                    gatePassed,
                    matchedWord,
                    replacement,
                    changed));
            }

            result.Add(changed ? cue with { Text = text } : cue);
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

    private static bool TryGetImmediateAddresseeCueGender(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int currentCueId,
        string addresseeSpeaker,
        out SpeakerVoiceGender gender)
    {
        gender = SpeakerVoiceGender.Unknown;
        var position = -1;
        for (var index = 0; index < source.Count; index++)
        {
            if (source[index].Index == currentCueId)
            {
                position = index;
                break;
            }
        }

        if (position < 0 || position + 1 >= source.Count)
            return false;

        var next = source[position + 1];
        if (!cueSpeakers.TryGetValue(next.Index, out var nextSpeaker) ||
            !string.Equals(nextSpeaker, addresseeSpeaker, StringComparison.Ordinal) ||
            !cueGenderEvidence.TryGetValue(next.Index, out var evidence) ||
            evidence.Gender == SpeakerVoiceGender.Unknown ||
            evidence.Confidence < MinimumCueGenderConfidence ||
            evidence.CombinedEvidence < MinimumCueCombinedEvidence ||
            evidence.DurationSeconds < MinimumCueDurationSeconds)
        {
            return false;
        }

        gender = evidence.Gender;
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

    private static string? FindGenderedCandidate(string text)
    {
        foreach (Match match in WordRegex().Matches(text))
        {
            var lower = match.Value.ToLowerInvariant();
            if (SpeakerMaleToFemale.ContainsKey(lower) ||
                SpeakerFemaleToMale.ContainsKey(lower) ||
                AddresseeMaleToFemale.ContainsKey(lower) ||
                AddresseeFemaleToMale.ContainsKey(lower) ||
                GenderedSuffixes.Any(suffix =>
                    lower.EndsWith(suffix, StringComparison.Ordinal) && lower.Length > suffix.Length))
            {
                return match.Value;
            }
        }

        return null;
    }

    private static (string? From, string? To) FindFirstChangedWordPair(string before, string after)
    {
        var beforeWords = WordRegex().Matches(before).Select(match => match.Value).ToArray();
        var afterWords = WordRegex().Matches(after).Select(match => match.Value).ToArray();
        var count = Math.Min(beforeWords.Length, afterWords.Length);
        for (var index = 0; index < count; index++)
        {
            if (!string.Equals(beforeWords[index], afterWords[index], StringComparison.Ordinal))
                return (beforeWords[index], afterWords[index]);
        }

        return (null, null);
    }

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
