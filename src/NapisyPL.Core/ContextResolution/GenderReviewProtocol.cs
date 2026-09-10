using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public static class GenderReviewProtocol
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string BuildPrompt(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        ContextMap context,
        IReadOnlyDictionary<int, string?> cueSpeakers)
    {
        if (source.Count != translated.Count)
            throw new ArgumentException("Source and translated cue counts must match.");

        var speakers = context.Speakers.ToDictionary(
            pair => pair.Key,
            pair => new
            {
                gender = pair.Value.Gender.ToString().ToLowerInvariant(),
                confidence = pair.Value.Confidence
            });

        var lines = source.Zip(translated).Select(pair =>
        {
            cueSpeakers.TryGetValue(pair.First.Index, out var speaker);
            context.Lines.TryGetValue(pair.First.Index, out var lineContext);
            return new
            {
                id = pair.First.Index,
                speaker,
                addressee = lineContext?.Addressee,
                addresseeConfidence = lineContext?.Confidence,
                source = pair.First.Text,
                polish = pair.Second.Text
            };
        });

        return $$"""
        /no_think
        You are a conservative Polish subtitle grammar reviewer.
        Review ONLY grammatical gender and singular/plural agreement that can be supported by the supplied speaker/addressee metadata and English source.
        Do NOT restyle, paraphrase, censor, improve tone, alter names, or rewrite already-correct lines.
        If evidence is uncertain, leave the line unchanged.
        Return ONLY a JSON array containing CHANGED lines. If nothing should change, return [].
        Exact output shape: [{"id":123,"text":"corrected Polish subtitle"}]
        Never output explanations or chain of thought.

        Speaker metadata:
        {{JsonSerializer.Serialize(speakers, JsonOptions)}}

        Subtitle lines:
        {{JsonSerializer.Serialize(lines, JsonOptions)}}
        """;
    }

    public static IReadOnlyDictionary<int, string> ParseResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("Gender reviewer returned an empty response.");

        var json = StripFence(response.Trim());
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Gender reviewer response must be a JSON array.");

            var result = new Dictionary<int, string>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id) || id <= 0 ||
                    !item.TryGetProperty("text", out var textElement) || textElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("Gender reviewer returned an invalid changed-line entry.");

                var text = textElement.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    throw new InvalidDataException($"Gender reviewer returned empty text for cue {id}.");
                if (!result.TryAdd(id, text))
                    throw new InvalidDataException($"Gender reviewer returned duplicate cue id {id}.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Gender reviewer returned invalid JSON.", ex);
        }
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstNewLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? text[(firstNewLine + 1)..lastFence].Trim()
            : text;
    }
}
