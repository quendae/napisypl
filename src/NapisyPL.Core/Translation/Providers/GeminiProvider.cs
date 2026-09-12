using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class GeminiProvider(HttpClient httpClient, string apiKey, string model, string? baseUrl = null) : ITranslationProvider
{
    private readonly string _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? "https://generativelanguage.googleapis.com/v1beta" : baseUrl.TrimEnd('/');
    public string DisplayName => "Gemini";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/interactions");
        request.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new { model, store = false, input = LlmJsonProtocol.BuildPrompt(segments) });

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Gemini: {(int)response.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var texts = new List<string>();
        if (doc.RootElement.TryGetProperty("steps", out var steps))
        {
            foreach (var step in steps.EnumerateArray())
            {
                if (!step.TryGetProperty("type", out var type) || type.GetString() != "model_output") continue;
                if (!step.TryGetProperty("content", out var content)) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var partType) && partType.GetString() == "text" && part.TryGetProperty("text", out var text))
                        texts.Add(text.GetString() ?? string.Empty);
                }
            }
        }
        if (texts.Count == 0)
            throw new InvalidDataException("Gemini nie zwrócił tekstowej odpowiedzi.");
        return LlmJsonProtocol.ParseResponse(string.Join("\n", texts));
    }
}
