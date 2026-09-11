using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record SurgicalGenderEdit(int Id, string Find, string Replace, double Confidence);

public static class SurgicalGenderEditProtocol
{
    public static IReadOnlyList<SurgicalGenderEdit> ParseResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("Gender reviewer returned an empty response.");

        var json = StripFence(response.Trim());
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Gender reviewer response must be a JSON array.");

            var result = new List<SurgicalGenderEdit>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id) || id <= 0 ||
                    !item.TryGetProperty("find", out var findElement) || findElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("replace", out var replaceElement) || replaceElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("confidence", out var confidenceElement) || !confidenceElement.TryGetDouble(out var confidence))
                    throw new InvalidDataException("Gender reviewer returned an invalid surgical-edit entry.");

                var find = findElement.GetString();
                var replace = replaceElement.GetString();
                if (string.IsNullOrWhiteSpace(find) || string.IsNullOrWhiteSpace(replace) || confidence is < 0 or > 1)
                    throw new InvalidDataException("Gender reviewer returned an invalid surgical-edit value.");

                result.Add(new SurgicalGenderEdit(id, find, replace, confidence));
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

public static class SurgicalGenderEditApplier
{
    public const double MinimumConfidence = 0.85;
    private const int MaxWordsPerSide = 3;
    private const int MaxCharsPerSide = 60;

    public static bool TryApply(SubtitleCue cue, SurgicalGenderEdit edit, out SubtitleCue changed)
    {
        changed = cue;
        if (edit.Id != cue.Index || edit.Confidence < MinimumConfidence)
            return false;
        if (!IsSmallFragment(edit.Find) || !IsSmallFragment(edit.Replace) || string.Equals(edit.Find, edit.Replace, StringComparison.Ordinal))
            return false;

        var first = cue.Text.IndexOf(edit.Find, StringComparison.Ordinal);
        if (first < 0)
            return false;
        var second = cue.Text.IndexOf(edit.Find, first + edit.Find.Length, StringComparison.Ordinal);
        if (second >= 0)
            return false;

        var updated = cue.Text[..first] + edit.Replace + cue.Text[(first + edit.Find.Length)..];
        changed = cue with { Text = updated };
        return true;
    }

    private static bool IsSmallFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCharsPerSide || value.Contains('\n') || value.Contains('\r'))
            return false;
        return value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length <= MaxWordsPerSide;
    }
}
