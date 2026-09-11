using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation.Providers;

namespace NapisyPL.Core.Tests;

public sealed class ArgosOfflineProviderTests
{
    [Fact]
    public async Task TranslateAsync_MapsReturnedTextsBackToSegmentIds()
    {
        var client = new FakeArgosClient(["Cześć", "Do widzenia"]);
        var provider = new ArgosOfflineProvider(client);
        var segments = new[]
        {
            new TranslationSegment(7, "Hello"),
            new TranslationSegment(11, "Goodbye")
        };

        var result = await provider.TranslateAsync(segments);

        Assert.Equal("Cześć", result[7]);
        Assert.Equal("Do widzenia", result[11]);
        Assert.Equal("Argos EN→PL", provider.DisplayName);
        Assert.Equal(40, provider.BatchPolicy.MaxSegments);
    }

    [Fact]
    public async Task TranslateAsync_RejectsMismatchedResultCount()
    {
        var provider = new ArgosOfflineProvider(new FakeArgosClient(["Tylko jeden"]));

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.TranslateAsync([
            new TranslationSegment(1, "One"),
            new TranslationSegment(2, "Two")
        ]));
    }

    private sealed class FakeArgosClient(IReadOnlyList<string> result) : IArgosTranslatorClient
    {
        public Task<IReadOnlyList<string>> TranslateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
