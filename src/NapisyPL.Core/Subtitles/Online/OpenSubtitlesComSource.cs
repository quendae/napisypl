using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles.Online;

/// <summary>
/// OpenSubtitles.com REST API with the user's own API key. Without a user login it allows
/// a few downloads a day per IP, so callers download only the best-ranked candidates.
/// </summary>
public sealed class OpenSubtitlesComSource(HttpClient httpClient, string apiKey, string userAgent) : IOnlineSubtitleSource
{
    public const string SourceName = "OpenSubtitles";
    private static readonly Uri BaseUri = new("https://api.opensubtitles.com/api/v1/");

    public string Name => SourceName;

    public async Task<IReadOnlyList<OnlineSubtitleCandidate>> SearchAsync(
        OnlineSubtitleQuery query,
        SubtitleLanguage language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        using var request = CreateRequest(HttpMethod.Get, "subtitles?" + BuildSearchQuery(query, language));
        using var response = await SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseSearch(json, language);
    }

    public async Task<IReadOnlyList<SubtitleCue>> DownloadAsync(
        OnlineSubtitleCandidate candidate,
        OnlineSubtitleQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        using var request = CreateRequest(HttpMethod.Post, "download");
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { file_id = long.Parse(candidate.Token, CultureInfo.InvariantCulture) }),
            Encoding.UTF8,
            "application/json");
        using var response = await SendAsync(request, cancellationToken);
        var link = ParseDownloadLink(await response.Content.ReadAsStringAsync(cancellationToken));

        var bytes = await httpClient.GetByteArrayAsync(link, cancellationToken);
        return SubtitlePayload.Read(bytes, query.Release);
    }

    /// <summary>The API redirects unless parameters are lower-case and sorted by name.</summary>
    public static string BuildSearchQuery(OnlineSubtitleQuery query, SubtitleLanguage language)
    {
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["languages"] = language == SubtitleLanguage.Polish ? "pl" : "en"
        };
        if (!string.IsNullOrWhiteSpace(query.MovieHash))
            parameters["moviehash"] = query.MovieHash!;
        if (!string.IsNullOrWhiteSpace(query.Release.Title))
            parameters["query"] = query.Release.Title.ToLowerInvariant();
        if (query.Release.IsEpisode)
        {
            parameters["season_number"] = query.Release.Season!.Value.ToString(CultureInfo.InvariantCulture);
            parameters["episode_number"] = query.Release.Episode!.Value.ToString(CultureInfo.InvariantCulture);
        }
        else if (query.Release.Year is { } year)
        {
            parameters["year"] = year.ToString(CultureInfo.InvariantCulture);
        }

        return string.Join("&", parameters.Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value)));
    }

    public static IReadOnlyList<OnlineSubtitleCandidate> ParseSearch(string json, SubtitleLanguage language)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return [];

        var candidates = new List<OnlineSubtitleCandidate>();
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("attributes", out var attributes))
                continue;
            if (!attributes.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                continue;

            // Multi-CD releases do not fit one video file.
            var fileList = files.EnumerateArray().ToArray();
            if (fileList.Length != 1 || !fileList[0].TryGetProperty("file_id", out var fileId))
                continue;

            var release = String(attributes, "release");
            if (string.IsNullOrWhiteSpace(release))
                release = String(fileList[0], "file_name");

            candidates.Add(new OnlineSubtitleCandidate(
                SourceName,
                language,
                release ?? string.Empty,
                Bool(attributes, "moviehash_match"),
                Bool(attributes, "hearing_impaired"),
                Int(attributes, "download_count"),
                fileId.ValueKind == JsonValueKind.Number
                    ? fileId.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : fileId.GetString() ?? string.Empty));
        }

        return candidates.Where(candidate => candidate.Token.Length > 0).ToArray();
    }

    public static Uri ParseDownloadLink(string json)
    {
        using var document = JsonDocument.Parse(json);
        var link = String(document.RootElement, "link");
        if (string.IsNullOrWhiteSpace(link) || !Uri.TryCreate(link, UriKind.Absolute, out var uri))
        {
            var message = String(document.RootElement, "message");
            throw new OnlineSubtitleSourceException("OpenSubtitles nie podał linku do pobrania" +
                                                    (string.IsNullOrWhiteSpace(message) ? "." : ": " + message));
        }
        return uri;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relative)
    {
        var request = new HttpRequestMessage(method, new Uri(BaseUri, relative));
        request.Headers.TryAddWithoutValidation("Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return response;

        var status = response.StatusCode;
        response.Dispose();
        throw new OnlineSubtitleSourceException(status switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "OpenSubtitles odrzucił klucz API. Sprawdź go w Opcjach → Źródła napisów.",
            HttpStatusCode.NotAcceptable or HttpStatusCode.TooManyRequests => "OpenSubtitles: wyczerpany dzienny limit pobrań. Spróbuj jutro.",
            _ => $"OpenSubtitles odpowiedział błędem {(int)status}."
        });
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static int Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
}
