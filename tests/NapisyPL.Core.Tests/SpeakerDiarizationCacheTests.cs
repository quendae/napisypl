using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerDiarizationCacheTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsSegmentsForUnchangedMedia()
    {
        var root = Path.Combine(Path.GetTempPath(), "SubFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var media = Path.Combine(root, "episode.mkv");
        await File.WriteAllTextAsync(media, "media-v1");
        var cache = new SpeakerDiarizationCache(Path.Combine(root, "cache"), "sherpa-pyannote3-campplus-v1");
        var segments = new[]
        {
            new SpeakerSegment(0.5, 1.5, 0),
            new SpeakerSegment(2.0, 3.0, 1)
        };

        await cache.SaveAsync(media, segments);
        var loaded = await cache.TryLoadAsync(media);

        Assert.NotNull(loaded);
        Assert.Equal(2, loaded!.Count);
        Assert.Equal(1, loaded[1].Speaker);
    }

    [Fact]
    public async Task Load_ReturnsNullWhenMediaFingerprintChanges()
    {
        var root = Path.Combine(Path.GetTempPath(), "SubFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var media = Path.Combine(root, "episode.mkv");
        await File.WriteAllTextAsync(media, "media-v1");
        var cache = new SpeakerDiarizationCache(Path.Combine(root, "cache"), "sherpa-pyannote3-campplus-v1");
        await cache.SaveAsync(media, new[] { new SpeakerSegment(0.5, 1.5, 0) });

        await File.AppendAllTextAsync(media, "-changed");
        var loaded = await cache.TryLoadAsync(media);

        Assert.Null(loaded);
    }
}
