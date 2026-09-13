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
        "Firefox/Bergamot (offline)" => new(false, false, false, false),
        "OPUS-MT / Marian (offline)" => new(false, false, false, false),
        "NLLB 600M — Fast" => new(false, false, false, false),
        "NLLB 1.3B — Balanced" => new(false, false, false, false),
        "NLLB 3.3B — Quality Test" => new(false, false, false, false),
        "MADLAD-400 3B — Quality" => new(false, false, false, false),
        "NLLB-200 600M (offline, benchmark)" => new(false, false, false, false),
        "Local Qwen — pełne tłumaczenie (wolne, eksperymentalne)" => new(false, false, false, false),
        "Local Qwen (offline)" => new(false, false, false, false),
        "DeepL" => new(true, false, true, true),
        "Gemini" => new(true, true, true, true),
        "Claude" => new(true, true, true, true),
        "OpenAI / Ollama" => new(true, true, true, true),
        _ => new(true, true, true, true)
    };
}
