using System.Text.Json;
using System.Text.RegularExpressions;
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

public static partial class SurgicalGenderEditApplier
{
    public const double MinimumConfidence = 0.85;
    private const int MaxWordsPerSide = 3;
    private const int MaxCharsPerSide = 60;

    public static bool TryApply(SubtitleCue cue, SurgicalGenderEdit edit, out SubtitleCue changed)
    {
        changed = cue;
        if (edit.Id != cue.Index || edit.Confidence < MinimumConfidence)
            return false;
        if (!IsSmallFragment(edit.Find) || !IsSmallFragment(edit.Replace) ||
            string.Equals(edit.Find, edit.Replace, StringComparison.Ordinal) ||
            !LooksLikeInflectionOnly(edit.Find, edit.Replace))
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
        return WordRegex().Matches(value).Count is > 0 and <= MaxWordsPerSide;
    }

    private static bool LooksLikeInflectionOnly(string find, string replace)
    {
        var sourceWords = WordRegex().Matches(find).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var replacementWords = WordRegex().Matches(replace).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (sourceWords.Length == 0 || sourceWords.Length != replacementWords.Length)
            return false;

        var changedWordCount = 0;
        for (var i = 0; i < sourceWords.Length; i++)
        {
            var source = sourceWords[i];
            var replacement = replacementWords[i];
            if (string.Equals(source, replacement, StringComparison.Ordinal))
                continue;

            changedWordCount++;
            var shorterLength = Math.Min(source.Length, replacement.Length);
            if (shorterLength < 2)
                return false;

            var commonPrefix = CommonPrefixLength(source, replacement);
            var minimumPrefix = Math.Max(2, shorterLength / 2);
            if (commonPrefix < minimumPrefix)
                return false;

            // Gender/number inflection normally changes an ending, not most of the lexical stem.
            if (source.Length - commonPrefix > 5 || replacement.Length - commonPrefix > 5)
                return false;
        }

        return changedWordCount is > 0 and <= MaxWordsPerSide;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
            index++;
        return index;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}
