using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class LocalQwenProvider(
    HttpClient httpClient,
    string baseUrl,
    string model) : ITranslationProvider
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public string DisplayName => "Local Qwen";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LocalLlm;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return new Dictionary<int, string>();

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model,
                temperature = 0.0,
                max_tokens = 3000,
                reasoning_effort = "none",
                chat_template_kwargs = new { enable_thinking = false },
                response_format = LlamaJsonSchemas.TranslationResponseFormat,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Translate English movie subtitles into natural Polish. Preserve meaning, tone and subtitle brevity. Return JSON only."
                    },
                    new
                    {
                        role = "user",
                        content = "/no_think\n" + LlmJsonProtocol.BuildPrompt(segments)
                    }
                }
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Local Qwen: {(int)response.StatusCode} {body}");

        try
        {
            using var document = JsonDocument.Parse(body);
            var choice = document.RootElement.GetProperty("choices")[0];
            var finishReason = choice.TryGetProperty("finish_reason", out var finishReasonElement)
                ? finishReasonElement.GetString()
                : null;
            if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Local Qwen przerwał odpowiedź przez limit tokenów. Spróbuj ponownie z mniejszą partią.");

            var content = choice.GetProperty("message").GetProperty("content").GetString();
            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("Local Qwen nie zwrócił treści tłumaczenia.");

            return LlmJsonProtocol.ParseResponse(content);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Local Qwen zwrócił nieprawidłową odpowiedź API.", ex);
        }
    }
}
