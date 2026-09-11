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
        IReadOnlyDictionary<string, IReadOnlyList<string>> speakerSamples)
    {
        if (sourceWindow.Count != translatedWindow.Count)
            throw new ArgumentException("Source and translated context windows must have equal length.");
        if (candidateIds.Count == 0)
            throw new ArgumentException("At least one candidate id is required.", nameof(candidateIds));

        var lines = sourceWindow.Zip(translatedWindow).Select(pair =>
        {
            cueSpeakers.TryGetValue(pair.First.Index, out var speaker);
            return new
            {
                id = pair.First.Index,
                candidate = candidateIds.Contains(pair.First.Index),
                speaker,
                source = pair.First.Text,
                polish = pair.Second.Text
            };
        }).ToArray();

        return $$"""
        /no_think
        You are a conservative Polish subtitle grammatical-agreement reviewer.
        The subtitles were already translated. Do NOT translate or rewrite the whole dialogue.

        Your only task is to correct clearly wrong Polish grammatical gender or singular/plural agreement in candidate lines.
        You may change ONLY IDs listed in candidateIds. Context lines are read-only.

        Evidence you may use:
        - the English source and current Polish translation,
        - stable speaker IDs obtained by audio diarization,
        - nearby dialogue and turn-taking,
        - explicit names, pronouns, titles and relationships in the text,
        - the supplied sample utterances belonging to the same speaker.

        Rules:
        - Never infer gender from voice pitch or from the numeric speaker ID.
        - Do not restyle, paraphrase, censor, improve tone, punctuation, names or vocabulary.
        - If gender/addressee is not sufficiently supported, leave the candidate unchanged.
        - Return ONLY changed candidate lines as a JSON array.
        - If nothing should change, return [].
        - Exact item shape: {"id":123,"text":"corrected Polish subtitle"}
        - No explanations or chain of thought.

        candidateIds:
        {{JsonSerializer.Serialize(candidateIds.OrderBy(id => id).ToArray(), JsonOptions)}}

        speakerSamples:
        {{JsonSerializer.Serialize(speakerSamples, JsonOptions)}}

        dialogueWindow:
        {{JsonSerializer.Serialize(lines, JsonOptions)}}
        """;
    }
}
