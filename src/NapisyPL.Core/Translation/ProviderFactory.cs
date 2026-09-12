using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Translation.Providers;

namespace NapisyPL.Core.Translation;

public static class ProviderFactory
{
    public static ITranslationProvider Create(
        HttpClient httpClient,
        string provider,
        string apiKey,
        string model,
        string baseUrl,
        IAppLogger? logger = null)
    {
        ITranslationProvider inner = provider switch
        {
            "DeepL" => new DeepLProvider(httpClient, RequireKey(provider, apiKey), baseUrl),
            "Gemini" => new GeminiProvider(httpClient, RequireKey(provider, apiKey), RequireModel(provider, model), baseUrl),
            "Claude" => new AnthropicProvider(httpClient, RequireKey(provider, apiKey), RequireModel(provider, model), baseUrl),
            "OpenAI / Ollama" => new OpenAiCompatibleProvider(httpClient, apiKey, RequireModel(provider, model), RequireBaseUrl(provider, baseUrl)),
            "Local Qwen (offline)" => new LocalQwenProvider(httpClient, RequireBaseUrl(provider, baseUrl), RequireModel(provider, model)),
            "Local Argos (offline)" => new ArgosOfflineProvider(ArgosRuntimeRegistry.GetOrCreate(httpClient)),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Nieznany provider tłumaczenia.")
        };

        return new LoggingTranslationProvider(inner, logger ?? new AppLogger());
    }

    private static string RequireKey(string provider, string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{provider}: podaj klucz API.") : value.Trim();

    private static string RequireModel(string provider, string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{provider}: podaj nazwę modelu.") : value.Trim();

    private static string RequireBaseUrl(string provider, string value) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{provider}: podaj Base URL.") : value.Trim();
}
