using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.OfflineMt.Nllb;
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
            "NLLB 600M — Fast" => new OfflineMachineTranslationProvider(NllbRuntimeRegistry.GetOrCreate(httpClient, NllbModelProfile.Fast600M)),
            "NLLB 1.3B — Balanced" => new OfflineMachineTranslationProvider(NllbRuntimeRegistry.GetOrCreate(httpClient, NllbModelProfile.Balanced1_3B)),
            "NLLB 3.3B — Quality Test" => new OfflineMachineTranslationProvider(NllbRuntimeRegistry.GetOrCreate(httpClient, NllbModelProfile.QualityNllb3_3B)),
            "MADLAD-400 3B — Quality" => new OfflineMachineTranslationProvider(NllbRuntimeRegistry.GetOrCreate(httpClient, NllbModelProfile.QualityMadlad3B)),
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
