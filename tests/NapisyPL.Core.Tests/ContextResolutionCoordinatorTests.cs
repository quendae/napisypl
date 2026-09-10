using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class ContextResolutionCoordinatorTests
{
    [Fact]
    public async Task ResolveAsync_UsesOverlappingWindowsAndKeepsHigherConfidence()
    {
        var cues = Enumerable.Range(1, 75)
            .Select(i => new ContextCue(i, i % 2 == 0 ? "SPEAKER_00" : "SPEAKER_01", $"Line {i}", null))
            .ToArray();
        var resolver = new FakeResolver();

        var result = await new ContextResolutionCoordinator(windowSize: 40, overlap: 10)
            .ResolveAsync(cues, resolver);

        Assert.Equal(3, resolver.Requests.Count);
        Assert.Equal(1, resolver.Requests[0][0].Id);
        Assert.Equal(31, resolver.Requests[1][0].Id);
        Assert.Equal(61, resolver.Requests[2][0].Id);
        Assert.Equal(SpeakerGender.Female, result.Speakers["SPEAKER_01"].Gender);
        Assert.Equal(0.9, result.Speakers["SPEAKER_01"].Confidence, 2);
    }

    private sealed class FakeResolver : IContextResolver
    {
        public List<IReadOnlyList<ContextCue>> Requests { get; } = [];

        public Task<ContextMap> ResolveAsync(IReadOnlyList<ContextCue> cues, CancellationToken cancellationToken = default)
        {
            Requests.Add(cues.ToArray());
            var confidence = Requests.Count == 1 ? 0.4 : 0.9;
            var gender = Requests.Count == 1 ? SpeakerGender.Unknown : SpeakerGender.Female;
            return Task.FromResult(new ContextMap(
                new Dictionary<string, SpeakerContext>
                {
                    ["SPEAKER_01"] = new(gender, confidence)
                },
                new Dictionary<int, LineContext>()));
        }
    }
}
