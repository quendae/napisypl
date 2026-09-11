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
    public async Task TranslateAsync_SplitsMultiSentenceCue_ThenReassemblesSameCue()
    {
        var client = new RecordingArgosClient(texts => texts.Select(text => text switch
        {
            "She blamed me for it." => "Obwiniła mnie za to.",
            "That is serious." => "To poważna sprawa.",
            _ => throw new InvalidOperationException(text)
        }).ToArray());
        var provider = new ArgosOfflineProvider(client);

        var result = await provider.TranslateAsync([
            new TranslationSegment(18, "She blamed me for it. That is serious.")
        ]);

        Assert.Equal(["She blamed me for it.", "That is serious."], client.LastTexts);
        Assert.Equal("Obwiniła mnie za to. To poważna sprawa.", result[18]);
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

    private sealed class RecordingArgosClient(Func<IReadOnlyList<string>, IReadOnlyList<string>> translate) : IArgosTranslatorClient
    {
        public IReadOnlyList<string> LastTexts { get; private set; } = [];

        public Task<IReadOnlyList<string>> TranslateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            LastTexts = texts.ToArray();
            return Task.FromResult(translate(texts));
        }
    }
}
