using System.Net;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class OfflineMtProviderCompositionTests
{
    [Theory]
    [InlineData("Firefox/Bergamot (offline)", "Firefox/Bergamot EN→PL")]
    [InlineData("OPUS-MT / Marian (offline)", "OPUS-MT / Marian EN→PL")]
    [InlineData("NLLB 600M — Fast", "NLLB 600M — Fast")]
    [InlineData("NLLB 1.3B — Balanced", "NLLB 1.3B — Balanced")]
    [InlineData("MADLAD-400 3B — Quality", "MADLAD-400 3B — Quality")]
    [InlineData("NLLB-200 600M (offline, benchmark)", "NLLB 600M — Fast")]
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
    [InlineData("Firefox/Bergamot (offline)")]
    [InlineData("OPUS-MT / Marian (offline)")]
    [InlineData("NLLB 600M — Fast")]
    [InlineData("NLLB 1.3B — Balanced")]
    [InlineData("MADLAD-400 3B — Quality")]
    [InlineData("NLLB-200 600M (offline, benchmark)")]
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
