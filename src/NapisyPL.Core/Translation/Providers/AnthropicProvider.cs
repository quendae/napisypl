using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class AnthropicProvider(HttpClient httpClient, string apiKey, string model, string? baseUrl = null) : ITranslationProvider
{
    private readonly string _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://api.anthropic.com" : baseUrl.TrimEnd('/');
    public string DisplayName => "Claude";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/v1/messages");
        request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        request.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(new
        {
            model,
            max_tokens = 8192,
            system = "Jesteś precyzyjnym tłumaczem napisów filmowych EN→PL. Zwracaj wyłącznie żądany JSON.",
            messages = new[] { new { role = "user", content = LlmJsonProtocol.BuildPrompt(segments) } }
        });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Claude: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var texts = doc.RootElement.GetProperty("content").EnumerateArray()
            .Where(x => x.TryGetProperty("type", out var type) && type.GetString() == "text")
            .Select(x => x.GetProperty("text").GetString() ?? string.Empty)
            .ToArray();
        if (texts.Length == 0)
            throw new InvalidDataException("Claude nie zwrócił tekstowej odpowiedzi.");
        return LlmJsonProtocol.ParseResponse(string.Join("\n", texts));
    }
}
