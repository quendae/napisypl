namespace NapisyPL.Core.Translation;

public sealed record ProviderUiProfile(
    bool ShowApiKey,
    bool ShowModel,
    bool ShowBaseUrl,
    bool CanRememberApiKey)
{
    public static ProviderUiProfile For(string provider) => provider switch
    {
        "Local Argos (offline)" => new(false, false, false, false),
        "Local Qwen — pełne tłumaczenie (wolne, eksperymentalne)" => new(false, false, false, false),
        "Local Qwen (offline)" => new(false, false, false, false),
        "DeepL" => new(true, false, true, true),
        "Gemini" => new(true, true, true, true),
        "Claude" => new(true, true, true, true),
        "OpenAI / Ollama" => new(true, true, true, true),
        _ => new(true, true, true, true)
    };
}
