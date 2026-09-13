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

    [Fact]
    public async Task TranslateCuesAsync_ReportsProviderWaitLifecycle()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(1), "Hello"),
            new SubtitleCue(2, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "World")
        };
        var reports = new List<TranslationProgress>();
        var progress = new InlineProgress<TranslationProgress>(reports.Add);

        await new TranslationCoordinator().TranslateCuesAsync(cues, new FakeProvider(), progress);

        var started = Assert.Single(reports, x => x.BatchIndex == 1 && x.WaitingForProvider);
        var completed = Assert.Single(reports, x => x.BatchIndex == 1 && !x.WaitingForProvider);
        Assert.Equal(0, started.CompletedSegments);
        Assert.Equal(2, completed.CompletedSegments);
        Assert.Equal(2, completed.TotalSegments);
        Assert.Equal(1, completed.BatchCount);
        Assert.Equal(started.BatchStartedAt, completed.BatchStartedAt);
    }

    [Fact]
    public async Task TranslateCuesAsync_UsesProviderBatchPolicy()
    {
        var cues = Enumerable.Range(1, 5)
            .Select(index => new SubtitleCue(index, TimeSpan.FromSeconds(index), TimeSpan.FromSeconds(index + 1), $"Line {index}"))
            .ToArray();
        var provider = new CountingProvider(new TranslationBatchPolicy(2, 1000));

        var result = await new TranslationCoordinator().TranslateCuesAsync(cues, provider);

        Assert.Equal(5, result.Count);
        Assert.Equal(3, provider.CallCount);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        public string DisplayName => "fake";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, string> result = segments.ToDictionary(x => x.Id, x => "PL: " + x.Text);
            return Task.FromResult(result);
        }
    }

    private sealed class MissingSegmentProvider : ITranslationProvider
    {
        public string DisplayName => "missing";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, string> result = new Dictionary<int, string> { [1] = "Jeden" };
            return Task.FromResult(result);
        }
    }

    private sealed class CountingProvider(TranslationBatchPolicy batchPolicy) : ITranslationProvider
    {
        public int CallCount { get; private set; }
        public string DisplayName => "counting";
        public TranslationBatchPolicy BatchPolicy => batchPolicy;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
        {
            CallCount++;
            IReadOnlyDictionary<int, string> result = segments.ToDictionary(x => x.Id, x => "PL: " + x.Text);
            return Task.FromResult(result);
        }
    }
}
