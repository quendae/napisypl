using System.Text.RegularExpressions;

namespace NapisyPL.Core.Translation;

public static partial class SubtitleMarkupNormalizer
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalized = SimpleTagRegex().Replace(text, match =>
        {
            var slash = match.Groups[1].Value;
            var tag = match.Groups[2].Value.ToLowerInvariant();
            return $"<{slash}{tag}>";
        });

        normalized = OpeningTagPaddingRegex().Replace(normalized, "<$1>");
        normalized = ClosingTagPaddingRegex().Replace(normalized, "</$1>");
        normalized = RemoveOrphanedTags(normalized);
        return normalized;
    }

    private static string RemoveOrphanedTags(string text)
    {
        var matches = NormalizedTagRegex().Matches(text);
        if (matches.Count == 0)
            return text;

        var balances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in matches)
        {
            var tag = match.Groups[2].Value;
            balances.TryGetValue(tag, out var balance);
            balances[tag] = balance + (match.Groups[1].Value.Length == 0 ? 1 : -1);
        }

        var unbalanced = balances
            .Where(pair => pair.Value != 0)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (unbalanced.Count == 0)
            return text;

        return NormalizedTagRegex().Replace(
            text,
            match => unbalanced.Contains(match.Groups[2].Value) ? string.Empty : match.Value);
    }

    [GeneratedRegex(@"<\s*(/?)\s*(i|b|u|s)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimpleTagRegex();

    [GeneratedRegex(@"<(/?)(i|b|u|s)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NormalizedTagRegex();

    [GeneratedRegex(@"<(i|b|u|s)>\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpeningTagPaddingRegex();

    [GeneratedRegex(@"\s+</(i|b|u|s)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosingTagPaddingRegex();
}
