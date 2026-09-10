using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class TranslationCoordinatorTests
{
    [Fact]
    public async Task TranslateCuesAsync_PreservesTimingsAndUsesReturnedText()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Hello"),
            new SubtitleCue(2, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), "Bye")
        };
        var provider = new FakeProvider();

        var result = await new TranslationCoordinator().TranslateCuesAsync(cues, provider);

        Assert.Equal("PL: Hello", result[0].Text);
        Assert.Equal(cues[0].Start, result[0].Start);
        Assert.Equal(cues[1].End, result[1].End);
    }

    [Fact]
    public async Task TranslateCuesAsync_RejectsMissingSegment()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "One"),
            new SubtitleCue(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "Two")
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new TranslationCoordinator().TranslateCuesAsync(cues, new MissingSegmentProvider()));
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        public string DisplayName => "fake";
        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, string> result = segments.ToDictionary(x => x.Id, x => "PL: " + x.Text);
            return Task.FromResult(result);
        }
    }

    private sealed class MissingSegmentProvider : ITranslationProvider
    {
        public string DisplayName => "missing";
        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, string> result = new Dictionary<int, string> { [1] = "Jeden" };
            return Task.FromResult(result);
        }
    }
}
