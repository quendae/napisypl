using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public static class LlmJsonProtocol
{
    private sealed record ResponseSegment(int Id, string Text);

    public static string BuildPrompt(IReadOnlyList<TranslationSegment> segments)
    {
        var payload = JsonSerializer.Serialize(segments, JsonOptions);
        return """
        Przetłumacz poniższe angielskie kwestie napisów na naturalny język polski.
        Zachowaj sens, ton, imiona własne, krótką formę napisów oraz podziały linii wewnątrz segmentu.
        Nie dodawaj komentarzy, objaśnień ani cudzysłowów. Nie tłumacz tagów formatowania, jeśli występują.
        Zwróć WYŁĄCZNIE tablicę JSON w identycznym formacie i z dokładnie tymi samymi wartościami id:
        [{"id":1,"text":"..."}]

        Dane:
        """ + payload;
    }

    public static IReadOnlyDictionary<int, string> ParseResponse(string text)
    {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = cleaned.IndexOf('\n');
            var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                cleaned = cleaned[(firstNewLine + 1)..lastFence].Trim();
        }

        var firstBracket = cleaned.IndexOf('[');
        var lastBracket = cleaned.LastIndexOf(']');
        if (firstBracket < 0 || lastBracket < firstBracket)
            throw new InvalidDataException("Model nie zwrócił oczekiwanej tablicy JSON.");

        cleaned = cleaned[firstBracket..(lastBracket + 1)];
        var items = JsonSerializer.Deserialize<List<ResponseSegment>>(cleaned, JsonOptions)
                    ?? throw new InvalidDataException("Pusta odpowiedź JSON od modelu.");
        var result = new Dictionary<int, string>();
        foreach (var item in items)
        {
            if (!result.TryAdd(item.Id, item.Text ?? string.Empty))
                throw new InvalidDataException($"Model zwrócił zduplikowane id {item.Id}.");
        }
        return result;
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}
