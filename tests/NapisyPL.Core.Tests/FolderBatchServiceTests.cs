using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class FolderBatchServiceTests
{
    [Fact]
    public async Task TranslateFolderAsync_RunsSequentiallyAndContinuesAfterFileFailure()
    {
        var root = CreateTempDirectory();
        try
        {
            foreach (var name in new[] { "a.srt", "b.srt", "c.srt" })
                File.WriteAllText(Path.Combine(root, name), "1\n00:00:00,000 --> 00:00:01,000\nHello\n");

            var pipeline = new FakePipeline(path => Path.GetFileName(path) == "b.srt");
            var service = new FolderBatchService(new FolderQueuePlanner(), new FakeProbe(), pipeline, NullAppLogger.Instance);

            var result = await service.TranslateFolderAsync(root, new FakeProvider(), exportTxt: false);

            Assert.Equal(["a.srt", "b.srt", "c.srt"], pipeline.Calls.Select(Path.GetFileName).ToArray());
            Assert.Equal(2, result.Translated);
            Assert.Equal(0, result.Skipped);
            Assert.Equal(1, result.Failed);
            Assert.Equal(FolderFileStatus.Failed, result.Files.Single(x => Path.GetFileName(x.InputPath) == "b.srt").Status);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task TranslateFolderAsync_SkipsExistingOutputWithoutCallingPipeline()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "episode.srt"), "input");
            File.WriteAllText(Path.Combine(root, "episode.pl.srt"), "already translated");
            var pipeline = new FakePipeline();
            var service = new FolderBatchService(new FolderQueuePlanner(), new FakeProbe(), pipeline, NullAppLogger.Instance);

            var result = await service.TranslateFolderAsync(root, new FakeProvider(), exportTxt: false);

            Assert.Empty(pipeline.Calls);
            Assert.Equal(1, result.Skipped);
            Assert.Equal(FolderFileStatus.Skipped, Assert.Single(result.Files).Status);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task TranslateFolderAsync_SkipsVideoWithNoSubtitleTrack()
    {
        var root = CreateTempDirectory();
        try
        {
            var movie = Path.Combine(root, "movie.mkv");
            File.WriteAllText(movie, string.Empty);
            var pipeline = new FakePipeline();
            var probe = new FakeProbe { Result = [] };
            var service = new FolderBatchService(new FolderQueuePlanner(), probe, pipeline, NullAppLogger.Instance);

            var result = await service.TranslateFolderAsync(root, new FakeProvider(), exportTxt: false);

            Assert.Empty(pipeline.Calls);
            Assert.Equal(1, result.Skipped);
            Assert.Equal("no_subtitles", Assert.Single(result.Files).ReasonCode);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task TranslateFolderAsync_SkipsBitmapOnlyVideo()
    {
        var root = CreateTempDirectory();
        try
        {
            var movie = Path.Combine(root, "movie.mkv");
            File.WriteAllText(movie, string.Empty);
            var pipeline = new FakePipeline();
            var probe = new FakeProbe
            {
                Result = [new SubtitleTrack(2, "hdmv_pgs_subtitle", "eng", "English PGS", false)]
            };
            var service = new FolderBatchService(new FolderQueuePlanner(), probe, pipeline, NullAppLogger.Instance);

            var result = await service.TranslateFolderAsync(root, new FakeProvider(), exportTxt: false);

            Assert.Empty(pipeline.Calls);
            Assert.Equal("bitmap_only", Assert.Single(result.Files).ReasonCode);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public async Task TranslateFolderAsync_AutoSelectsEnglishTextTrackAndReportsProgress()
    {
        var root = CreateTempDirectory();
        try
        {
            var movie = Path.Combine(root, "movie.mkv");
            File.WriteAllText(movie, string.Empty);
            var pipeline = new FakePipeline();
            var probe = new FakeProbe
            {
                Result =
                [
                    new SubtitleTrack(1, "subrip", "deu", "Deutsch", true),
                    new SubtitleTrack(3, "subrip", "eng", "English", true)
                ]
            };
            var reports = new List<FolderBatchProgress>();
            var service = new FolderBatchService(new FolderQueuePlanner(), probe, pipeline, NullAppLogger.Instance);

            var result = await service.TranslateFolderAsync(
                root,
                new FakeProvider(),
                exportTxt: false,
                new InlineProgress<FolderBatchProgress>(reports.Add));

            Assert.Equal(3, Assert.Single(pipeline.Tracks)!.StreamIndex);
            Assert.Equal(1, result.Translated);
            Assert.Contains(reports, x => x.FileIndex == 1 && x.FileCount == 1 && x.FileName == "movie.mkv");
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private sealed class FakeProbe : IMediaProbeService
    {
        public IReadOnlyList<SubtitleTrack> Result { get; set; } = [];

        public Task<IReadOnlyList<SubtitleTrack>> ProbeAsync(string mediaPath, IProgress<string>? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result);
    }

    private sealed class FakePipeline(Func<string, bool>? shouldThrow = null) : ITranslationPipeline
    {
        public List<string> Calls { get; } = [];
        public List<SubtitleTrack?> Tracks { get; } = [];

        public Task<TranslationResult> TranslateAsync(
            string inputPath,
            SubtitleTrack? selectedTrack,
            ITranslationProvider provider,
            bool exportTxt,
            IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(inputPath);
            Tracks.Add(selectedTrack);
            if (shouldThrow?.Invoke(inputPath) == true)
                throw new InvalidOperationException("simulated file failure");

            var output = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                Path.GetFileNameWithoutExtension(inputPath) + (Path.GetExtension(inputPath).Equals(".txt", StringComparison.OrdinalIgnoreCase) ? ".pl.txt" : ".pl.srt"));
            return Task.FromResult(new TranslationResult(output, null, 1));
        }
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        public string DisplayName => "fake";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(segments.ToDictionary(x => x.Id, x => x.Text));
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-folder-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
