using System.Net.Http.Json;
using System.Text.Json;
using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.ContextResolution;

public sealed class OpenAiContextResolver(
    HttpClient httpClient,
    string baseUrl,
    string model) : IContextResolver
{
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public async Task<ContextMap> ResolveAsync(
        IReadOnlyList<ContextCue> cues,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
            return new ContextMap(
                new Dictionary<string, SpeakerContext>(),
                new Dictionary<int, LineContext>());

        using var request = new HttpRequestMessage(HttpMethod.Post, _baseUrl + "/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                model,
                temperature = 0.0,
                max_tokens = 1200,
                reasoning_effort = "none",
                chat_template_kwargs = new { enable_thinking = false },
                response_format = LlamaJsonSchemas.ContextResponseFormat,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Classify subtitle dialogue context. Return metadata JSON only; never translate subtitles."
                    },
                    new
                    {
                        role = "user",
                        content = ContextResolverProtocol.BuildPrompt(cues)
                    }
                }
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Local context resolver: {(int)response.StatusCode} {body}");

        try
        {
            using var document = JsonDocument.Parse(body);
            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidDataException("Local context resolver returned no content.");

            return ContextResolverProtocol.ParseResponse(content);
        }
        catch (KeyNotFoundException ex)
        {
            throw new InvalidDataException("Local context resolver returned an unexpected response.", ex);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Local context resolver returned invalid API JSON.", ex);
        }
    }
}
