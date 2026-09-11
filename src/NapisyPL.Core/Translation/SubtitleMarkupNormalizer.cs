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
        return normalized;
    }

    [GeneratedRegex(@"<\s*(/?)\s*(i|b|u|s)\s*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SimpleTagRegex();

    [GeneratedRegex(@"<(i|b|u|s)>\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OpeningTagPaddingRegex();

    [GeneratedRegex(@"\s+</(i|b|u|s)>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClosingTagPaddingRegex();
}
