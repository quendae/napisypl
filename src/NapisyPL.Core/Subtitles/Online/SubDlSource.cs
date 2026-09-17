using System.Globalization;
using System.Net;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles.Online;

/// <summary>SubDL API with the user's own API key. Downloads are ZIP files from dl.subdl.com.</summary>
public sealed class SubDlSource(HttpClient httpClient, string apiKey) : IOnlineSubtitleSource
{
    public const string SourceName = "SubDL";
    private const string SearchEndpoint = "https://api.subdl.com/api/v1/subtitles";
    private static readonly Uri DownloadBase = new("https://dl.subdl.com");

    public string Name => SourceName;

    public async Task<IReadOnlyList<OnlineSubtitleCandidate>> SearchAsync(
        OnlineSubtitleQuery query,
        SubtitleLanguage language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var response = await httpClient.GetAsync(SearchEndpoint + "?" + BuildSearchQuery(apiKey, query, language), cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new OnlineSubtitleSourceException("SubDL odrzucił klucz API. Sprawdź go w Opcjach → Źródła napisów.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new OnlineSubtitleSourceException("SubDL: za dużo zapytań. Spróbuj za chwilę.");
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new OnlineSubtitleSourceException($"SubDL odpowiedział błędem {(int)response.StatusCode}.");
        return ParseSearch(json, language, query.Release);
    }

    public async Task<IReadOnlyList<SubtitleCue>> DownloadAsync(
        OnlineSubtitleCandidate candidate,
        OnlineSubtitleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var bytes = await httpClient.GetByteArrayAsync(new Uri(DownloadBase, candidate.Token), cancellationToken);
        return SubtitlePayload.Read(bytes, query.Release);
    }

    public static string BuildSearchQuery(string apiKey, OnlineSubtitleQuery query, SubtitleLanguage language)
    {
        var parameters = new List<(string Name, string Value)>
        {
            ("api_key", apiKey),
            ("film_name", query.Release.Title),
            ("file_name", Path.GetFileName(query.VideoPath)),
            ("languages", language == SubtitleLanguage.Polish ? "PL" : "EN"),
            ("subs_per_page", "30"),
            ("releases", "1")
        };
        if (query.Release.IsEpisode)
        {
            parameters.Add(("type", "tv"));
            parameters.Add(("season_number", query.Release.Season!.Value.ToString(CultureInfo.InvariantCulture)));
            parameters.Add(("episode_number", query.Release.Episode!.Value.ToString(CultureInfo.InvariantCulture)));
        }
        else
        {
            parameters.Add(("type", "movie"));
            if (query.Release.Year is { } year)
                parameters.Add(("year", year.ToString(CultureInfo.InvariantCulture)));
        }

        return string.Join("&", parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => parameter.Name + "=" + Uri.EscapeDataString(parameter.Value)));
    }

    public static IReadOnlyList<OnlineSubtitleCandidate> ParseSearch(string json, SubtitleLanguage language, ReleaseName video)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.False)
            return [];
        if (!root.TryGetProperty("subtitles", out var subtitles) || subtitles.ValueKind != JsonValueKind.Array)
            return [];

        var wanted = language == SubtitleLanguage.Polish ? "PL" : "EN";
        var candidates = new List<OnlineSubtitleCandidate>();
        foreach (var item in subtitles.EnumerateArray())
        {
            var url = String(item, "url");
            if (string.IsNullOrWhiteSpace(url))
                continue;
            var itemLanguage = String(item, "language") ?? String(item, "lang") ?? wanted;
            if (!itemLanguage.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) &&
                !itemLanguage.StartsWith(language == SubtitleLanguage.Polish ? "pol" : "eng", StringComparison.OrdinalIgnoreCase))
                continue;
            // A whole-season pack is fine only when it can contain this episode.
            if (video.IsEpisode && Int(item, "episode") is > 0 and var episode && episode != video.Episode)
                continue;

            candidates.Add(new OnlineSubtitleCandidate(
                SourceName,
                language,
                String(item, "release_name") ?? String(item, "name") ?? string.Empty,
                IsHashMatch: false,
                HearingImpaired: item.TryGetProperty("hi", out var hi) && hi.ValueKind == JsonValueKind.True,
                DownloadCount: 0,
                Token: url.StartsWith('/') ? url : "/" + url));
        }

        return candidates;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
}
