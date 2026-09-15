using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class VideoSubtitleTranslationPipelineTests
{
    [Fact]
    public async Task TranslateVideoSubtitlesAsync_WritesUsingVideoStemAndPreservesSuppliedCueMetadata()
    {
        var directory = CreateTempDirectory();
        try
        {
            var videoPath = Path.Combine(directory, "episode.mkv");
            var sourceCues = new[]
            {
                new SubtitleCue(41, TimeSpan.FromSeconds(1.25), TimeSpan.FromSeconds(3.5), "Hello"),
                new SubtitleCue(93, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(6.75), "Bye")
            };
            var provider = new RecordingProvider();
            var pipeline = new TranslationPipeline(new SrtParser(), new SubtitleWriter(), null!, new TranslationCoordinator());

            var result = await pipeline.TranslateVideoSubtitlesAsync(videoPath, sourceCues, provider, exportTxt: true);

            Assert.Equal(Path.Combine(directory, "episode.pl.srt"), result.PrimaryOutputPath);
            Assert.Equal(Path.Combine(directory, "episode.pl.txt"), result.TextOutputPath);
            Assert.Equal([41, 93], provider.SegmentIds);

            var written = new SrtParser().Parse(await File.ReadAllTextAsync(result.PrimaryOutputPath));
            Assert.Equal(sourceCues.Select(cue => cue.Start), written.Select(cue => cue.Start));
            Assert.Equal(sourceCues.Select(cue => cue.End), written.Select(cue => cue.End));
            Assert.Equal(["PL: Hello", "PL: Bye"], written.Select(cue => cue.Text));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public async Task TranslateVideoSubtitlesAsync_WhenEnhancedEnabled_ForwardsOriginalVideoAndCuesToEnhancedPipeline()
    {
        var sourceCues = new[] { new SubtitleCue(7, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), "Hello") };
        var enhanced = new RecordingVideoPipeline();
        var pipeline = new TranslationPipeline(new SrtParser(), new SubtitleWriter(), null!, new TranslationCoordinator())
        {
            UseEnhanced = true,
            EnhancedPipeline = enhanced
        };

        var result = await pipeline.TranslateVideoSubtitlesAsync("C:\\videos\\episode.mkv", sourceCues, new RecordingProvider(), exportTxt: false);

        Assert.Equal(enhanced.Result, result);
        Assert.Equal("C:\\videos\\episode.mkv", enhanced.VideoPath);
        Assert.Same(sourceCues, enhanced.SourceCues);
    }

    private sealed class RecordingProvider : ITranslationProvider
    {
        public List<int> SegmentIds { get; } = [];
        public string DisplayName => "recording";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(
            IReadOnlyList<TranslationSegment> segments,
            CancellationToken cancellationToken = default)
        {
            SegmentIds.AddRange(segments.Select(segment => segment.Id));
            IReadOnlyDictionary<int, string> translated = segments.ToDictionary(segment => segment.Id, segment => "PL: " + segment.Text);
            return Task.FromResult(translated);
        }
    }

    private sealed class RecordingVideoPipeline : IVideoSubtitleTranslationPipeline
    {
        public TranslationResult Result { get; } = new("enhanced.pl.srt", null, 1);
        public string? VideoPath { get; private set; }
        public IReadOnlyList<SubtitleCue>? SourceCues { get; private set; }

        public Task<TranslationResult> TranslateAsync(
            string inputPath,
            SubtitleTrack? selectedTrack,
            ITranslationProvider provider,
            bool exportTxt,
            IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);

        public Task<TranslationResult> TranslateVideoSubtitlesAsync(
            string videoPath,
            IReadOnlyList<SubtitleCue> sourceCues,
            ITranslationProvider provider,
            bool exportTxt,
            IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            VideoPath = videoPath;
            SourceCues = sourceCues;
            return Task.FromResult(Result);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "NapisyPL-video-subtitles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
