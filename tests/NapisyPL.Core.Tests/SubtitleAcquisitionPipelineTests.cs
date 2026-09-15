using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class SubtitleAcquisitionPipelineTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NapisyPL-acquisition-" + Guid.NewGuid().ToString("N"));
    private static readonly SubtitleCue[] English = [new(7, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "You did it.")];
    private static readonly SubtitleCue[] Polish = [new(7, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), "Zrobiłaś to.")];
    private string Video => Path.Combine(_directory, "film.mkv");
    private string Output => Path.Combine(_directory, "film.pl.srt");
    public SubtitleAcquisitionPipelineTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task PolishDownloadWritesUtf8AndTxtWithoutInitializingTranslator()
    {
        var downloader = new FakeDownloader(language => new(language, "QNapi", Polish));
        var inner = new FakePipeline();
        var service = new SubtitleAcquisitionPipeline(inner, downloader);
        var result = await service.TranslateAsync(Video, null, NoTranslator(), true);
        Assert.Equal(Output, result.PrimaryOutputPath);
        Assert.Contains("Zrobiłaś to.", await File.ReadAllTextAsync(Output));
        Assert.Contains("00:00:03,000 --> 00:00:05,000", await File.ReadAllTextAsync(Output));
        Assert.Equal("Zrobiłaś to.", await File.ReadAllTextAsync(result.TextOutputPath!));
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
        Assert.Null(inner.VideoPath);
        Assert.False(inner.StandardCalled);
    }

    [Fact]
    public async Task EnglishFallbackPreservesOriginalVideoAndCueIdentity()
    {
        var downloader = new FakeDownloader(language => language == SubtitleLanguage.Polish ? null : new(language, "QNapi", English));
        var inner = new FakePipeline();
        await new SubtitleAcquisitionPipeline(inner, downloader).TranslateAsync(Video, null, NoTranslator(), false);
        Assert.Equal([SubtitleLanguage.Polish, SubtitleLanguage.English], downloader.Calls);
        Assert.Equal(Video, inner.VideoPath);
        Assert.Same(English, inner.Cues);
        Assert.False(inner.StandardCalled);
    }

    [Fact]
    public async Task NoDownloadsUsesExistingEmbeddedTrack()
    {
        var inner = new FakePipeline();
        var track = new SubtitleTrack(4, "subrip", "eng", null, true);
        await new SubtitleAcquisitionPipeline(inner, new FakeDownloader(_ => null))
            .TranslateAsync(Video, track, NoTranslator(), false);
        Assert.True(inner.StandardCalled);
        Assert.Same(track, inner.Track);
        Assert.Equal(Video, inner.VideoPath);
    }

    [Fact]
    public async Task ServiceFailureReportsProblemAndCanUseEmbeddedTrack()
    {
        var messages = new List<string>();
        var inner = new FakePipeline();
        var downloader = new FakeDownloader(_ => throw new HttpRequestException("offline"));
        await new SubtitleAcquisitionPipeline(inner, downloader).TranslateAsync(Video,
            new SubtitleTrack(2, "subrip", "eng", null, true), NoTranslator(), false,
            status: new InlineProgress<string>(messages.Add));
        Assert.True(inner.StandardCalled);
        Assert.Contains(messages, m => m.Contains("offline", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancellationDoesNotTryEnglishOrEmbeddedTrack()
    {
        var inner = new FakePipeline();
        var downloader = new FakeDownloader(_ => throw new OperationCanceledException());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SubtitleAcquisitionPipeline(inner, downloader)
            .TranslateAsync(Video, new SubtitleTrack(1, "subrip", "eng", null, true), NoTranslator(), false));
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
        Assert.False(inner.StandardCalled);
    }

    [Fact]
    public async Task MissingAllSourcesGivesActionableErrorWithoutOutput()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SubtitleAcquisitionPipeline(new FakePipeline(), new FakeDownloader(_ => null))
                .TranslateAsync(Video, null, NoTranslator(), false));
        Assert.Contains("napis", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public async Task ExistingPolishOutputIsKeptAndCanBeExportedAsTxt()
    {
        var existing = new SubtitleWriter().BuildSrt(Polish);
        await File.WriteAllTextAsync(Output, existing);
        var downloader = new FakeDownloader(_ => throw new Exception("must not search"));
        var result = await new SubtitleAcquisitionPipeline(new FakePipeline(), downloader)
            .TranslateAsync(Video, null, NoTranslator(), true);
        Assert.Equal(existing, await File.ReadAllTextAsync(Output));
        Assert.Equal("Zrobiłaś to.", await File.ReadAllTextAsync(result.TextOutputPath!));
        Assert.Empty(downloader.Calls);
    }

    [Fact]
    public async Task InvalidExistingFileIsNotOverwritten()
    {
        await File.WriteAllTextAsync(Output, "existing invalid file");
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SubtitleAcquisitionPipeline(new FakePipeline(), new FakeDownloader(_ => new(SubtitleLanguage.Polish, "QNapi", Polish)))
                .TranslateAsync(Video, null, NoTranslator(), false));
        Assert.Equal("existing invalid file", await File.ReadAllTextAsync(Output));
    }

    [Theory]
    [InlineData(false, "film.mkv")]
    [InlineData(true, "captions.srt")]
    public async Task DisabledOrStandaloneInputDoesNotSearch(bool enabled, string name)
    {
        var downloader = new FakeDownloader(_ => throw new Exception("must not search"));
        var inner = new FakePipeline();
        var service = new SubtitleAcquisitionPipeline(inner, downloader) { Enabled = enabled };
        await service.TranslateAsync(Path.Combine(_directory, name), null, NoTranslator(), false);
        Assert.Empty(downloader.Calls);
        Assert.True(inner.StandardCalled);
    }

    [Fact]
    public async Task WrongLanguageResultIsRejectedAndEnglishAttemptContinues()
    {
        var downloader = new FakeDownloader(_ => new(SubtitleLanguage.English, "QNapi", English));
        var inner = new FakePipeline();
        await new SubtitleAcquisitionPipeline(inner, downloader).TranslateAsync(Video, null, NoTranslator(), false);
        Assert.Equal([SubtitleLanguage.Polish, SubtitleLanguage.English], downloader.Calls);
        Assert.Same(English, inner.Cues);
        Assert.False(File.Exists(Output));
    }

    private static ITranslationProvider NoTranslator() => new DeferredTranslationProvider(() => throw new Exception("Translator must stay lazy"));
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private sealed class FakeDownloader(Func<SubtitleLanguage, DownloadedSubtitles?> get) : ISubtitleDownloader
    {
        public List<SubtitleLanguage> Calls { get; } = [];
        public Task<DownloadedSubtitles?> DownloadAsync(string videoPath, SubtitleLanguage language, IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(language);
            return Task.FromResult(get(language));
        }
    }
    private sealed class FakePipeline : IVideoSubtitleTranslationPipeline
    {
        public string? VideoPath { get; private set; }
        public IReadOnlyList<SubtitleCue>? Cues { get; private set; }
        public bool StandardCalled { get; private set; }
        public SubtitleTrack? Track { get; private set; }
        public Task<TranslationResult> TranslateVideoSubtitlesAsync(string videoPath, IReadOnlyList<SubtitleCue> sourceCues,
            ITranslationProvider provider, bool exportTxt, IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            VideoPath = videoPath;
            Cues = sourceCues;
            return Task.FromResult(new TranslationResult(videoPath + ".pl.srt", null, sourceCues.Count));
        }
        public Task<TranslationResult> TranslateAsync(string inputPath, SubtitleTrack? selectedTrack,
            ITranslationProvider provider, bool exportTxt, IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            StandardCalled = true;
            VideoPath = inputPath;
            Track = selectedTrack;
            return Task.FromResult(new TranslationResult(inputPath + ".pl.srt", null, 1));
        }
    }
}
