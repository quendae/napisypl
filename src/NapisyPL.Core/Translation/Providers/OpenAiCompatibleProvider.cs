using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class OpenAiCompatibleProvider(HttpClient httpClient, string? apiKey, string model, string baseUrl) : ITranslationProvider
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    public string DisplayName => "OpenAI-compatible";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions");
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "Jesteś precyzyjnym tłumaczem napisów filmowych EN→PL. Zwracaj wyłącznie żądany JSON." },
                new { role = "user", content = LlmJsonProtocol.BuildPrompt(segments) }
            }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"OpenAI-compatible: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var content = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        if (string.IsNullOrWhiteSpace(content))
            throw new InvalidDataException("Provider OpenAI-compatible nie zwrócił treści.");
        return LlmJsonProtocol.ParseResponse(content);
    }
}
