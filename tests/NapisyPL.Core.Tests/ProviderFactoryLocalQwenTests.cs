using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class ProviderFactoryLocalQwenTests
{
    [Fact]
    public void Create_LocalQwen_DoesNotRequireApiKey()
    {
        using var http = new HttpClient();

        var provider = ProviderFactory.Create(
            http,
            "Local Qwen (offline)",
            apiKey: string.Empty,
            model: "qwen3-1.7b",
            baseUrl: "http://127.0.0.1:17843/v1");

        Assert.Equal("Local Qwen", provider.DisplayName);
        Assert.Equal(10, provider.BatchPolicy.MaxSegments);
    }
}
