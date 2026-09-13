using System.Text.Encodings.Web;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public static class TargetedGenderReviewProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string BuildPrompt(
        IReadOnlyList<SubtitleCue> sourceWindow,
        IReadOnlyList<SubtitleCue> translatedWindow,
        IReadOnlySet<int> candidateIds,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, IReadOnlyList<string>> speakerSamples,
        IReadOnlyDictionary<int, string?>? probableAddressees = null,
        IReadOnlyDictionary<string, SpeakerGenderEvidence>? speakerGenderEvidence = null)
    {
        if (sourceWindow.Count != translatedWindow.Count)
            throw new ArgumentException("Source and translated context windows must have equal length.");
        if (candidateIds.Count == 0)
            throw new ArgumentException("At least one candidate id is required.", nameof(candidateIds));

        var lines = sourceWindow.Zip(translatedWindow).Select(pair =>
        {
            cueSpeakers.TryGetValue(pair.First.Index, out var speaker);
            var isCandidate = candidateIds.Contains(pair.First.Index);
            var probableAddressee = isCandidate && probableAddressees is not null &&
                                    probableAddressees.TryGetValue(pair.First.Index, out var resolvedAddressee)
                ? resolvedAddressee
                : null;

            string? candidateSpeakerGender = null;
            double? candidateSpeakerGenderConfidence = null;
            if (isCandidate &&
                !string.IsNullOrWhiteSpace(speaker) &&
                speakerGenderEvidence is not null &&
                speakerGenderEvidence.TryGetValue(speaker, out var candidateEvidence) &&
                SpeakerGenderReviewEligibility.IsEligible(candidateEvidence))
            {
                candidateSpeakerGender = candidateEvidence.Gender.ToString().ToLowerInvariant();
                candidateSpeakerGenderConfidence = Math.Round(candidateEvidence.Confidence, 3);
            }

            string? candidateAddresseeGender = null;
            double? candidateAddresseeGenderConfidence = null;
            if (isCandidate &&
                !string.IsNullOrWhiteSpace(probableAddressee) &&
                speakerGenderEvidence is not null &&
                speakerGenderEvidence.TryGetValue(probableAddressee, out var addresseeEvidence) &&
                SpeakerGenderReviewEligibility.IsEligible(addresseeEvidence))
            {
                candidateAddresseeGender = addresseeEvidence.Gender.ToString().ToLowerInvariant();
                candidateAddresseeGenderConfidence = Math.Round(addresseeEvidence.Confidence, 3);
            }

            return new
            {
                id = pair.First.Index,
                candidate = isCandidate,
                speaker,
                candidateSpeakerGender,
                candidateSpeakerGenderConfidence,
                probableAddressee,
                candidateAddresseeGender,
                candidateAddresseeGenderConfidence,
                source = pair.First.Text,
                polish = pair.Second.Text
            };
        }).ToArray();

        var mandatoryAddresseeChecks = lines
            .Where(line =>
                line.candidate &&
                !string.IsNullOrWhiteSpace(line.probableAddressee) &&
                !string.IsNullOrWhiteSpace(line.candidateAddresseeGender))
            .Select(line => new
            {
                line.id,
                line.probableAddressee,
                line.candidateAddresseeGender,
                line.candidateAddresseeGenderConfidence,
                line.source,
                line.polish
            })
            .ToArray();

        var relevantSpeakerIds = lines
            .Where(line => line.candidate)
            .SelectMany(line => new[] { line.speaker, line.probableAddressee })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);

        var relevantGenderEvidence = (speakerGenderEvidence ?? new Dictionary<string, SpeakerGenderEvidence>())
            .Where(pair => relevantSpeakerIds.Contains(pair.Key))
            .ToDictionary(
                pair => pair.Key,
                pair => new
                {
                    gender = pair.Value.Gender.ToString().ToLowerInvariant(),
                    confidence = Math.Round(pair.Value.Confidence, 3),
                    sampleCount = pair.Value.SampleCount
                },
                StringComparer.Ordinal);

        return $$"""
        /no_think
        You are a conservative Polish subtitle grammatical-agreement reviewer.
        The subtitles are already translated. Never rewrite a subtitle.

        Inspect ONLY candidateIds for clearly wrong Polish grammatical gender or singular/plural agreement.
        Context lines are read-only and exist only to identify speaker/addressee context.

        There are TWO different grammatical targets:
        - target="speaker": the changed Polish form describes the person SPEAKING the candidate line, e.g. "byłem" -> "byłam" or "zrobiłem" -> "zrobiłam".
        - target="addressee": the changed Polish form directly addresses the LISTENER, e.g. "byłeś" -> "byłaś" or "zrobiłeś" -> "zrobiłaś".

        IMPORTANT: the main ambiguity we are trying to resolve is often the gender of the person being ADDRESSED, not the gender of the person speaking.
        Stable speaker IDs are used to resolve who talks before/after a cue. Acoustic gender evidence belongs to those stable speaker identities and can therefore describe a probable addressee when that person also speaks elsewhere.

        speakerGenderEvidence is produced by a separate local acoustic classifier over diarized speech fragments.
        - It is included for diagnostics/context, but raw speakerGenderEvidence is NOT by itself permission to make an edit.
        - gender="unknown" is no evidence.
        - confidence describes consistency of the acoustic classification; do not treat it as certainty about identity.
        - Never derive gender yourself from voice pitch, speaker number, a name, stereotypes, or acoustic impressions beyond the supplied classifier result.
        - For speaker-target edits, use ONLY candidateSpeakerGender on that candidate as the actionable acoustic signal.
        - For addressee-target edits, use ONLY candidateAddresseeGender on that candidate as the actionable acoustic signal.
        - These candidate gender fields are present only when the corresponding stable speaker has male/female evidence with confidence >= 0.85 from at least 2 samples.
        - An eligible candidateSpeakerGender is sufficient on its own without explicit dialogue confirmation when the current Polish form clearly uses the opposite grammatical gender and there is no conflicting context evidence.
        - If supplied acoustic evidence conflicts with explicit dialogue evidence or remains uncertain, return no edit.

        IMPORTANT KNOWN-SPEAKER CHECK:
        - candidateSpeakerGender is copied directly onto a candidate only when that candidate's own speaker passes the application's speaker-gender safety gate.
        - For EVERY candidate with candidateSpeakerGender="male" or "female", you MUST perform the speaker-target check before deciding that no edit is needed.
        - For a candidate where candidateSpeakerGender is null, NEVER return target="speaker".
        - Compare the current Polish candidate line against candidateSpeakerGender. Check past-tense verbs, conditional/person forms, adjectives, participles and other Polish forms that grammatically describe the speaker.
        - If a speaker-describing Polish form clearly encodes the opposite gender, return the smallest exact replacement with target="speaker".
        - A gender-neutral English source is not a reason to abstain. English often omits speaker gender where Polish grammar requires it.
        - Do NOT require an English pronoun, title, relationship word or other textual gender confirmation when candidateSpeakerGender is present.
        - If the Polish candidate already matches candidateSpeakerGender, or the form is genuinely gender-neutral, return no speaker edit for that form.

        IMPORTANT KNOWN-ADDRESSEE CHECK:
        - probableAddressee is computed by the application only when conservative turn-taking heuristics resolve one likely listener: a strong B -> A -> B sandwich, a quick next-speaker reply inside a two-speaker window, or a stable two-speaker dialogue partner.
        - candidateAddresseeGender is the eligible gender of the probableAddressee, learned from that person's own diarized speech elsewhere; it is NOT the gender of the current speaker.
        - mandatoryAddresseeChecks is a short priority list containing ONLY candidate lines whose probable addressee already passed the application's acoustic safety gate.
        - BEFORE reviewing the remaining candidates, inspect EVERY item in mandatoryAddresseeChecks word-by-word for second-person Polish forms whose grammatical gender conflicts with candidateAddresseeGender.
        - Do not skip an item merely because the English source is gender-neutral. Forms such as zrobiłeś/zrobiłaś, byłeś/byłaś, chciałeś/chciałaś and zapomniałeś/zapomniałaś encode the listener's gender in Polish.
        - For EVERY candidate with probableAddressee non-null and candidateAddresseeGender="male" or "female", you MUST perform the addressee-target check before deciding that no edit is needed.
        - Compare second-person Polish forms in the candidate against the gender of the probableAddressee.
        - If a form directly addressing the listener clearly encodes the opposite gender, return the smallest exact replacement with target="addressee".
        - probableAddressee MUST be non-null and candidateAddresseeGender MUST be non-null for target="addressee".
        - Use ONLY that probableAddressee; never choose another context speaker.
        - If candidateAddresseeGender is null, NEVER return target="addressee".
        - Require very strong evidence and confidence >= 0.95 for an addressee edit.
        - If the Polish candidate already matches candidateAddresseeGender, or the form does not grammatically describe the addressee, return no addressee edit.

        Output ONLY minimal exact fragment replacements as JSON.
        Each item must be:
        {"id":123,"find":"zrobiłeś","replace":"zrobiłaś","confidence":0.97,"target":"addressee"}

        Hard rules:
        - find MUST be an exact substring copied from the current Polish candidate line.
        - find and replace MUST each contain at most 3 words.
        - Never return the whole subtitle sentence.
        - Never change punctuation, style, names, vocabulary or meaning.
        - Never copy text from another subtitle line.
        - Change only grammatical gender/number agreement.
        - Use stable speaker IDs only for turn identity; speaker IDs do NOT imply gender.
        - Never infer gender from speaker number, stereotypes, or a name alone.
        - Prefer explicit pronouns, gendered titles, relationships, candidateSpeakerGender, candidateAddresseeGender, or unambiguous dialogue evidence.
        - If evidence is uncertain, return no edit for that candidate.
        - Give confidence >= 0.85 only when a speaker-target correction is strongly supported.
        - Give confidence >= 0.95 only when an addressee-target correction is strongly supported.
        - If nothing should change, return [].
        - No explanations or chain of thought.

        mandatoryAddresseeChecks:
        {{JsonSerializer.Serialize(mandatoryAddresseeChecks, JsonOptions)}}

        candidateIds:
        {{JsonSerializer.Serialize(candidateIds.OrderBy(id => id).ToArray(), JsonOptions)}}

        relevantSpeakerSamples:
        {{JsonSerializer.Serialize(speakerSamples, JsonOptions)}}

        speakerGenderEvidence:
        {{JsonSerializer.Serialize(relevantGenderEvidence, JsonOptions)}}

        dialogueContext:
        {{JsonSerializer.Serialize(lines, JsonOptions)}}
        """;
    }
}
