using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class DeepLProvider(HttpClient httpClient, string apiKey, string? baseUrl = null) : ITranslationProvider
{
    private readonly string _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api-free.deepl.com" : baseUrl.TrimEnd('/');
    public string DisplayName => "DeepL";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.MachineTranslation;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v2/translate");
        request.Headers.TryAddWithoutValidation("Authorization", "DeepL-Auth-Key " + apiKey);
        request.Content = JsonContent.Create(new
        {
            text = segments.Select(s => s.Text).ToArray(),
            source_lang = "EN",
            target_lang = "PL",
            preserve_formatting = true
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"DeepL: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var translations = doc.RootElement.GetProperty("translations").EnumerateArray().ToArray();
        if (translations.Length != segments.Count)
            throw new InvalidDataException("DeepL zwrócił inną liczbę segmentów niż wysłano.");

        var result = new Dictionary<int, string>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
            result[segments[i].Id] = translations[i].GetProperty("text").GetString() ?? string.Empty;
        return result;
    }
}
