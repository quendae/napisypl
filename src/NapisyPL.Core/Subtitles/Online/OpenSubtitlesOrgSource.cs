using System.Globalization;
using System.Net;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles.Online;

/// <summary>
/// The legacy opensubtitles.org REST search (rest.opensubtitles.org), which still answers
/// without a key. It is being retired, so failures are reported and never block the search.
/// </summary>
public sealed class OpenSubtitlesOrgSource(HttpClient httpClient) : IOnlineSubtitleSource
{
    public const string SourceName = "OpenSubtitles.org";

    // The service's published agent for clients without their own registration.
    private const string LegacyUserAgent = "TemporaryUserAgent";
    private const string SearchBase = "https://rest.opensubtitles.org/search/";

    public string Name => SourceName;

    public async Task<IReadOnlyList<OnlineSubtitleCandidate>> SearchAsync(
        OnlineSubtitleQuery query,
        SubtitleLanguage language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var candidates = new List<OnlineSubtitleCandidate>();
        if (query.MovieHash is not null && TryGetFileSize(query.VideoPath) is { } size)
            candidates.AddRange(await SearchPathAsync(BuildHashPath(query.MovieHash, size, language), language, cancellationToken));
        if (!string.IsNullOrWhiteSpace(query.Release.Title))
            candidates.AddRange(await SearchPathAsync(BuildQueryPath(query.Release, language), language, cancellationToken));

        // The same file can come back from both searches; keep the hash match.
        return candidates
            .GroupBy(candidate => candidate.Token, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.IsHashMatch).First())
            .ToArray();
    }

    public async Task<IReadOnlyList<SubtitleCue>> DownloadAsync(
        OnlineSubtitleCandidate candidate,
        OnlineSubtitleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var request = new HttpRequestMessage(HttpMethod.Get, candidate.Token);
        request.Headers.TryAddWithoutValidation("X-User-Agent", LegacyUserAgent);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OnlineSubtitleSourceException(response.StatusCode == HttpStatusCode.TooManyRequests
                ? "OpenSubtitles.org: wyczerpany limit pobrań. Spróbuj później."
                : $"OpenSubtitles.org odpowiedział błędem {(int)response.StatusCode} przy pobieraniu.");
        return SubtitlePayload.Read(await response.Content.ReadAsByteArrayAsync(cancellationToken), query.Release);
    }

    /// <summary>Path segments must be lower-case and in alphabetical order.</summary>
    public static string BuildHashPath(string movieHash, long fileSize, SubtitleLanguage language) =>
        $"moviebytesize-{fileSize.ToString(CultureInfo.InvariantCulture)}/moviehash-{movieHash.ToLowerInvariant()}/sublanguageid-{LanguageId(language)}";

    public static string BuildQueryPath(ReleaseName release, SubtitleLanguage language)
    {
        var segments = new List<string>();
        if (release.IsEpisode)
            segments.Add("episode-" + release.Episode!.Value.ToString(CultureInfo.InvariantCulture));
        segments.Add("query-" + Uri.EscapeDataString(release.Title.ToLowerInvariant()));
        if (release.IsEpisode)
            segments.Add("season-" + release.Season!.Value.ToString(CultureInfo.InvariantCulture));
        segments.Add("sublanguageid-" + LanguageId(language));
        return string.Join("/", segments);
    }

    public static IReadOnlyList<OnlineSubtitleCandidate> ParseSearch(string json, SubtitleLanguage language)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var candidates = new List<OnlineSubtitleCandidate>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var link = String(item, "SubDownloadLink");
            if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link, UriKind.Absolute, out _))
                continue;
            if (!string.Equals(String(item, "SubFormat"), "srt", StringComparison.OrdinalIgnoreCase) ||
                String(item, "SubSumCD") is { } cds && cds != "1")
                continue;
            // Machine-translated uploads are exactly what SubFlow is trying to beat.
            if (String(item, "SubAutoTranslation") == "1")
                continue;

            var release = String(item, "MovieReleaseName")?.Trim();
            if (string.IsNullOrWhiteSpace(release))
                release = String(item, "SubFileName") ?? string.Empty;

            candidates.Add(new OnlineSubtitleCandidate(
                SourceName,
                language,
                release,
                IsHashMatch: string.Equals(String(item, "MatchedBy"), "moviehash", StringComparison.OrdinalIgnoreCase),
                HearingImpaired: String(item, "SubHearingImpaired") == "1",
                DownloadCount: int.TryParse(String(item, "SubDownloadsCnt"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0,
                Token: link));
        }

        return candidates;
    }

    private async Task<IReadOnlyList<OnlineSubtitleCandidate>> SearchPathAsync(string path, SubtitleLanguage language, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SearchBase + path);
        request.Headers.TryAddWithoutValidation("X-User-Agent", LegacyUserAgent);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new OnlineSubtitleSourceException(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.Gone
                ? "OpenSubtitles.org nie przyjmuje już zapytań bez rejestracji. Wyłącz to źródło w Opcjach → Źródła napisów."
                : $"OpenSubtitles.org odpowiedział błędem {(int)response.StatusCode}.");
        return ParseSearch(await response.Content.ReadAsStringAsync(cancellationToken), language);
    }

    private static long? TryGetFileSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string LanguageId(SubtitleLanguage language) => language == SubtitleLanguage.Polish ? "pol" : "eng";

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
