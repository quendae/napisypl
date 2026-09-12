using System.Net.Http.Headers;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class FirefoxRemoteSettingsClient
{
    public static Uri DefaultServerBaseUri { get; } = new("https://firefox.settings.services.mozilla.com/v2/", UriKind.Absolute);

    private readonly HttpClient _httpClient;
    private readonly Uri _serverBaseUri;

    public FirefoxRemoteSettingsClient(HttpClient httpClient, Uri? serverBaseUri = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _serverBaseUri = NormalizeBaseUri(serverBaseUri ?? DefaultServerBaseUri);
    }

    public async Task<BergamotModelDescriptor> ResolveModelAsync(
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        var requestUri = new Uri(
            _serverBaseUri,
            "buckets/main/collections/translations-models-v2/changeset?_expected=0");
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SubFlow", "1.0"));
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        var registryJson = await response.Content.ReadAsStringAsync(cancellationToken);

        return FirefoxBergamotModelResolver.Resolve(registryJson, sourceLanguage, targetLanguage);
    }

    private static Uri NormalizeBaseUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("Firefox Remote Settings server URI must be absolute.", nameof(uri));

        var text = uri.AbsoluteUri;
        return text.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(text + "/", UriKind.Absolute);
    }
}
