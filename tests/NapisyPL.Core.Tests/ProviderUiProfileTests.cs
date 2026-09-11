using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class ProviderUiProfileTests
{
    [Theory]
    [InlineData("Local Argos (offline)", false, false, false, false)]
    [InlineData("Local Qwen — pełne tłumaczenie (wolne, eksperymentalne)", false, false, false, false)]
    [InlineData("DeepL", true, false, true, true)]
    [InlineData("Gemini", true, true, true, true)]
    [InlineData("Claude", true, true, true, true)]
    [InlineData("OpenAI / Ollama", true, true, true, true)]
    public void For_ReturnsExpectedVisibility(
        string provider,
        bool showApiKey,
        bool showModel,
        bool showBaseUrl,
        bool canRememberApiKey)
    {
        var profile = ProviderUiProfile.For(provider);

        Assert.Equal(showApiKey, profile.ShowApiKey);
        Assert.Equal(showModel, profile.ShowModel);
        Assert.Equal(showBaseUrl, profile.ShowBaseUrl);
        Assert.Equal(canRememberApiKey, profile.CanRememberApiKey);
    }
}
