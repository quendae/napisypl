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
        IReadOnlyDictionary<int, string?>? probableAddressees = null)
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
            return new
            {
                id = pair.First.Index,
                candidate = isCandidate,
                speaker,
                probableAddressee,
                source = pair.First.Text,
                polish = pair.Second.Text
            };
        }).ToArray();

        return $$"""
        /no_think
        You are a conservative Polish subtitle grammatical-agreement reviewer.
        The subtitles are already translated. Never rewrite a subtitle.

        Inspect ONLY candidateIds for clearly wrong Polish grammatical gender or singular/plural agreement.
        Context lines are read-only and exist only to identify speaker/addressee context.

        There are TWO different grammatical targets:
        - target="speaker": the changed Polish form describes the person SPEAKING the candidate line, e.g. "byłem" -> "byłam" or "zrobiłem" -> "zrobiłam".
        - target="addressee": the changed Polish form directly addresses the LISTENER, e.g. "byłeś" -> "byłaś" or "zrobiłeś" -> "zrobiłaś".

        probableAddressee is computed by the application only when turn-taking strongly looks like B -> A -> B.
        For target="addressee":
        - probableAddressee MUST be non-null.
        - Use ONLY that speaker as the possible addressee; never choose another context speaker.
        - Require very strong explicit evidence and confidence >= 0.95.
        - If probableAddressee is null, NEVER edit an addressee-dependent form.

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
        - Never infer gender from voice pitch, speaker number, stereotypes, or a name alone.
        - Prefer explicit pronouns, gendered titles, relationships, or unambiguous dialogue evidence.
        - If evidence is uncertain, return no edit for that candidate.
        - Give confidence >= 0.85 only when a speaker-target correction is strongly supported.
        - Give confidence >= 0.95 only when an addressee-target correction is strongly supported.
        - If nothing should change, return [].
        - No explanations or chain of thought.

        candidateIds:
        {{JsonSerializer.Serialize(candidateIds.OrderBy(id => id).ToArray(), JsonOptions)}}

        relevantSpeakerSamples:
        {{JsonSerializer.Serialize(speakerSamples, JsonOptions)}}

        dialogueContext:
        {{JsonSerializer.Serialize(lines, JsonOptions)}}
        """;
    }
}
