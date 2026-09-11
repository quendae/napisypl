using System.Text.Json;

namespace NapisyPL.Core.ContextResolution;

public enum SpeakerGender
{
    Unknown,
    Male,
    Female,
    Mixed
}

public sealed record ContextCue(
    int Id,
    string? SpeakerId,
    string Text,
    string? SpeakerLabel);

public sealed record SpeakerContext(
    SpeakerGender Gender,
    double Confidence);

public sealed record LineContext(
    string? Addressee,
    double Confidence);

public sealed record ContextMap(
    IReadOnlyDictionary<string, SpeakerContext> Speakers,
    IReadOnlyDictionary<int, LineContext> Lines);

public static class ContextResolverProtocol
{
    private static readonly JsonSerializerOptions PromptJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string BuildPrompt(IReadOnlyList<ContextCue> cues)
    {
        if (cues.Count == 0)
            throw new ArgumentException("At least one context cue is required.", nameof(cues));

        var payload = cues.Select(cue => new
        {
            id = cue.Id,
            speaker = cue.SpeakerId,
            speakerLabel = cue.SpeakerLabel,
            text = cue.Text
        });

        return $$"""
        /no_think
        You are a subtitle dialogue context classifier. Do not translate or rewrite the subtitles.
        Infer only metadata useful for English-to-Polish grammatical agreement.

        Return ONLY valid JSON with this exact shape:
        {
          "speakers": {
            "SPEAKER_01": { "gender": "male|female|mixed|unknown", "confidence": 0.0 }
          },
          "lines": {
            "123": { "addressee": "SPEAKER_01|group|unknown", "confidence": 0.0 }
          }
        }

        Rules:
        - Use evidence from the dialogue, explicit names/titles/relations and turn-taking.
        - gender describes grammatical gender evidence for the speaker, not voice pitch.
        - Do not guess when evidence is weak: use unknown and a low confidence.
        - confidence must be between 0 and 1.
        - Include line metadata only when addressee information is useful and reasonably inferable.
        - Output metadata only. No commentary, markdown, translation, or chain of thought.

        Subtitle context JSON:
        {{JsonSerializer.Serialize(payload, PromptJsonOptions)}}
        """;
    }

    public static ContextMap ParseResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("Context resolver returned an empty response.");

        var json = ExtractJsonObject(StripFence(response.Trim()));
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Context resolver response must be a JSON object.");

            var speakers = ParseSpeakers(root);
            var lines = ParseLines(root);
            return new ContextMap(speakers, lines);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Context resolver returned invalid JSON.", ex);
        }
    }

    private static IReadOnlyDictionary<string, SpeakerContext> ParseSpeakers(JsonElement root)
    {
        var result = new Dictionary<string, SpeakerContext>(StringComparer.Ordinal);
        if (!root.TryGetProperty("speakers", out var speakersElement) || speakersElement.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in speakersElement.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid speaker metadata.");

            if (!property.Value.TryGetProperty("gender", out var genderElement) || genderElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"Speaker {property.Name} is missing gender.");

            var gender = ParseGender(genderElement.GetString());
            var confidence = ReadConfidence(property.Value, $"speaker {property.Name}");
            result[property.Name] = new SpeakerContext(gender, confidence);
        }

        return result;
    }

    private static IReadOnlyDictionary<int, LineContext> ParseLines(JsonElement root)
    {
        var result = new Dictionary<int, LineContext>();
        if (!root.TryGetProperty("lines", out var linesElement) || linesElement.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var property in linesElement.EnumerateObject())
        {
            if (!int.TryParse(property.Name, out var id) || id <= 0 || property.Value.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid line metadata id.");

            string? addressee = null;
            if (property.Value.TryGetProperty("addressee", out var addresseeElement))
            {
                if (addresseeElement.ValueKind is not JsonValueKind.String and not JsonValueKind.Null)
                    throw new InvalidDataException($"Line {id} has an invalid addressee.");
                addressee = addresseeElement.ValueKind == JsonValueKind.String ? addresseeElement.GetString() : null;
            }

            var confidence = ReadConfidence(property.Value, $"line {id}");
            result[id] = new LineContext(addressee, confidence);
        }

        return result;
    }

    private static SpeakerGender ParseGender(string? value) => value switch
    {
        "male" => SpeakerGender.Male,
        "female" => SpeakerGender.Female,
        "mixed" => SpeakerGender.Mixed,
        "unknown" => SpeakerGender.Unknown,
        _ => throw new InvalidDataException($"Unsupported speaker gender '{value}'.")
    };

    private static double ReadConfidence(JsonElement element, string field)
    {
        if (!element.TryGetProperty("confidence", out var confidenceElement) ||
            confidenceElement.ValueKind != JsonValueKind.Number ||
            !confidenceElement.TryGetDouble(out var confidence) ||
            double.IsNaN(confidence) ||
            confidence < 0 || confidence > 1)
        {
            throw new InvalidDataException($"Invalid confidence for {field}.");
        }

        return confidence;
    }

    private static string ExtractJsonObject(string text)
    {
        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');
        return firstBrace >= 0 && lastBrace >= firstBrace
            ? text[firstBrace..(lastBrace + 1)]
            : text;
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;

        var firstNewLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        if (firstNewLine < 0 || lastFence <= firstNewLine)
            return text;

        return text[(firstNewLine + 1)..lastFence].Trim();
    }
}
