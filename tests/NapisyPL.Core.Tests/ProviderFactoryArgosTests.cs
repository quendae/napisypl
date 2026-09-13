using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class ProviderFactoryArgosTests
{
    [Fact]
    public void Create_LocalArgos_DoesNotRequireApiKeyModelOrBaseUrl()
    {
        using var http = new HttpClient();

        var provider = ProviderFactory.Create(
            http,
            "Local Argos (offline)",
            apiKey: "",
            model: "",
            baseUrl: "");

        Assert.Equal("Argos EN→PL", provider.DisplayName);
        Assert.Equal(40, provider.BatchPolicy.MaxSegments);
    }
}
