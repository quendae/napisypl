using System.Text.RegularExpressions;

namespace NapisyPL.Core.Subtitles.Online;

/// <summary>
/// "Chance.S01E01.The.Summer.of.Love.1080p.WEB-DL-tamir.AC3-5.1.x265" → title "Chance",
/// season 1, episode 1, and the tokens that tell one release from another.
/// </summary>
public sealed partial record ReleaseName(
    string Title,
    int? Year,
    int? Season,
    int? Episode,
    IReadOnlySet<string> Tokens)
{
    private static readonly HashSet<string> SourceTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "webdl", "webrip", "web", "bluray", "bdrip", "brrip", "hdtv", "dvdrip", "remux", "hdrip", "amzn", "nf", "atvp", "dsnp", "hmax", "hulu"
    };

    public bool IsEpisode => Season is not null && Episode is not null;

    public static ReleaseName Parse(string fileNameOrRelease)
    {
        var name = Path.GetFileNameWithoutExtension(fileNameOrRelease ?? string.Empty);
        if (name.Length == 0)
            name = fileNameOrRelease ?? string.Empty;
        var normalized = SeparatorRegex().Replace(name, " ").Trim();

        int? season = null, episode = null, year = null;
        var cut = normalized.Length;

        var episodeMatch = EpisodeRegex().Match(normalized);
        if (!episodeMatch.Success)
            episodeMatch = AlternateEpisodeRegex().Match(normalized);
        if (episodeMatch.Success)
        {
            season = int.Parse(episodeMatch.Groups["season"].Value);
            episode = int.Parse(episodeMatch.Groups["episode"].Value);
            cut = Math.Min(cut, episodeMatch.Index);
        }

        var yearMatch = YearRegex().Match(normalized);
        if (yearMatch.Success && yearMatch.Index > 0)
        {
            year = int.Parse(yearMatch.Value);
            cut = Math.Min(cut, yearMatch.Index);
        }

        var qualityMatch = QualityRegex().Match(normalized);
        if (qualityMatch.Success && qualityMatch.Index > 0)
            cut = Math.Min(cut, qualityMatch.Index);

        // Release tokens start at the quality tag; the episode title between is not a release trait.
        var tokenStart = qualityMatch.Success && qualityMatch.Index >= cut ? qualityMatch.Index : cut;
        var title = normalized[..cut].Trim();
        var releasePart = WebDlRegex().Replace(normalized[tokenStart..].ToLowerInvariant(), "webdl");
        var tokens = TokenRegex().Matches(releasePart)
            .Select(match => match.Value)
            .Where(token => token.Length > 1 || char.IsDigit(token[0]))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ReleaseName(title, year, season, episode, tokens);
    }

    /// <summary>"The.Office.US" and "the office us" compare equal; punctuation and case do not matter.</summary>
    public static string NormalizeTitle(string title) =>
        string.Join(" ", TokenRegex().Matches(title.ToLowerInvariant()).Select(match => match.Value));

    public bool SameEpisodeAs(ReleaseName other) =>
        !IsEpisode || !other.IsEpisode || (Season == other.Season && Episode == other.Episode);

    /// <summary>0..1: shared release tokens, with the source (WEB-DL, BluRay) and group weighted up.</summary>
    public double Similarity(ReleaseName other)
    {
        if (Tokens.Count == 0 || other.Tokens.Count == 0)
            return 0;

        double shared = 0, total = 0;
        foreach (var token in Tokens.Union(other.Tokens, StringComparer.OrdinalIgnoreCase))
        {
            var weight = SourceTokens.Contains(token) ? 3 : 1;
            total += weight;
            if (Tokens.Contains(token) && other.Tokens.Contains(token))
                shared += weight;
        }

        return total == 0 ? 0 : shared / total;
    }

    [GeneratedRegex(@"[._\[\]()]+")]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\b[Ss](?<season>\d{1,2})\s?[Ee](?<episode>\d{1,3})\b")]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"\b(?<season>\d{1,2})[xX](?<episode>\d{2,3})\b")]
    private static partial Regex AlternateEpisodeRegex();

    [GeneratedRegex(@"\b(?:19|20)\d{2}\b")]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"\b(?:2160p|1080p|720p|480p|4k|web[- ]?dl|webrip|bluray|hdtv|dvdrip|bdrip|remux)\b", RegexOptions.IgnoreCase)]
    private static partial Regex QualityRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"web[- ]?dl")]
    private static partial Regex WebDlRegex();
}
