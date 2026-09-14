using System.Net;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class OfflineMtProviderCompositionTests
{
    [Theory]
    [InlineData("NLLB 600M — Fast", "NLLB 600M — Fast")]
    [InlineData("NLLB 1.3B — Balanced", "NLLB 1.3B — Balanced")]
    [InlineData("NLLB 3.3B — Quality Test", "NLLB 3.3B — Quality Test")]
    [InlineData("MADLAD-400 3B — Quality", "MADLAD-400 3B — Quality")]
    public void ProviderFactory_CreateOfflineMtProvider_IsLazyAndNeedsNoCloudSettings(
        string providerName,
        string expectedDisplayName)
    {
        using var httpClient = new HttpClient(new NoNetworkHandler());

        var provider = ProviderFactory.Create(
            httpClient,
            providerName,
            apiKey: string.Empty,
            model: string.Empty,
            baseUrl: string.Empty);

        Assert.Equal(expectedDisplayName, provider.DisplayName);
    }

    [Theory]
    [InlineData("NLLB 600M — Fast")]
    [InlineData("NLLB 1.3B — Balanced")]
    [InlineData("NLLB 3.3B — Quality Test")]
    [InlineData("MADLAD-400 3B — Quality")]
    public void ProviderUiProfile_OfflineMtProviders_HideCloudFields(string providerName)
    {
        var profile = ProviderUiProfile.For(providerName);

        Assert.False(profile.ShowApiKey);
        Assert.False(profile.ShowModel);
        Assert.False(profile.ShowBaseUrl);
        Assert.False(profile.CanRememberApiKey);
    }

    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException(
                $"Provider construction must not access network: {request.RequestUri}");
    }
}
