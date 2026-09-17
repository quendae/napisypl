using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

/// <summary>
/// The automatic search behind "Szukaj napisów z SubFlow": Polish from QNapi first,
/// then English inside the video, then English from QNapi, without translating.
/// </summary>
public sealed class SubtitleAvailabilityScanTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NapisyPL-scan-" + Guid.NewGuid().ToString("N"));
    private static readonly SubtitleTrack EnglishTrack = new(4, "subrip", "eng", null, true);

    public SubtitleAvailabilityScanTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    private string Video => Path.Combine(_directory, "film.mkv");
    private string Output => Path.Combine(_directory, "film.pl.srt");

    [Fact]
    public async Task ExistingPolishFile_IsKeptWithoutSearching()
    {
        await File.WriteAllTextAsync(Output, "1\n00:00:01,000 --> 00:00:02,000\nCześć.\n");
        var downloader = new Downloader(_ => null);

        var result = await Pipeline(downloader).ScanAsync(Video, EnglishTrack);

        Assert.Equal(PolishSubtitleState.Existing, result.Polish);
        Assert.True(result.HasPolish);
        Assert.Empty(downloader.Calls);
    }

    [Fact]
    public async Task PolishFromQnapi_IsSavedAndEnglishIsNotSearched()
    {
        var downloader = new Downloader(language => language == SubtitleLanguage.Polish
            ? new DownloadedSubtitles(language, "NapiProjekt", Cues(0, "Polski"))
            : null);

        var result = await Pipeline(downloader).ScanAsync(Video, null);

        Assert.Equal(PolishSubtitleState.Saved, result.Polish);
        Assert.Equal("NapiProjekt", result.PolishProvider);
        Assert.True(File.Exists(Output));
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
    }

    [Fact]
    public async Task NoPolish_EmbeddedEnglishIsTheSourceAndQnapiEnglishIsSkipped()
    {
        var embedded = Cues(0, "English");
        var downloader = new Downloader(_ => null);

        var result = await Pipeline(downloader, embedded).ScanAsync(Video, EnglishTrack);

        Assert.Equal(PolishSubtitleState.Missing, result.Polish);
        Assert.Equal(EnglishSubtitleSource.Embedded, result.English);
        Assert.Same(embedded, result.EnglishCues);
        Assert.True(result.CanTranslate);
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public async Task NoPolishAndNoEmbeddedEnglish_SearchesEnglishOnQnapi()
    {
        var english = Cues(0, "English");
        var downloader = new Downloader(language => language == SubtitleLanguage.English
            ? new DownloadedSubtitles(language, "NapiProjekt", english)
            : null);

        var result = await Pipeline(downloader).ScanAsync(Video, null);

        Assert.Equal(EnglishSubtitleSource.Downloaded, result.English);
        Assert.Equal("NapiProjekt", result.EnglishProvider);
        Assert.Same(english, result.EnglishCues);
        Assert.Equal([SubtitleLanguage.Polish, SubtitleLanguage.English], downloader.Calls);
    }

    [Fact]
    public async Task EmbeddedTrackThatIsNotEnglish_IsNotUsedAsTheSource()
    {
        var downloader = new Downloader(_ => null);

        var result = await Pipeline(downloader, Cues(0, "Deutsch"))
            .ScanAsync(Video, new SubtitleTrack(3, "subrip", "ger", null, true));

        Assert.Equal(EnglishSubtitleSource.None, result.English);
        Assert.Equal([SubtitleLanguage.Polish, SubtitleLanguage.English], downloader.Calls);
    }

    [Fact]
    public async Task NothingFound_CannotTranslate()
    {
        var result = await Pipeline(new Downloader(_ => null)).ScanAsync(Video, null);

        Assert.Equal(PolishSubtitleState.Missing, result.Polish);
        Assert.Equal(EnglishSubtitleSource.None, result.English);
        Assert.False(result.CanTranslate);
        Assert.False(result.HasPolish);
    }

    [Fact]
    public async Task PolishWithUnsafeTiming_IsNotWrittenAndEnglishStaysAvailable()
    {
        var reference = Cues(0, "English");
        var unsafePolish = reference
            .Select((cue, position) => new SubtitleCue(cue.Index,
                cue.Start.Add(TimeSpan.FromMilliseconds(position % 2 == 0 ? 0 : 900)),
                cue.End.Add(TimeSpan.FromMilliseconds(position % 2 == 0 ? 0 : 900)), "Polski " + cue.Index))
            .ToArray();
        var downloader = new Downloader(language => language == SubtitleLanguage.Polish
            ? new DownloadedSubtitles(language, "Napisy24", unsafePolish)
            : null);

        var result = await Pipeline(downloader, reference).ScanAsync(Video, EnglishTrack);

        Assert.Equal(PolishSubtitleState.NeedsReview, result.Polish);
        Assert.False(result.HasPolish);
        Assert.False(File.Exists(Output));
        Assert.Equal(EnglishSubtitleSource.Embedded, result.English);
    }

    [Fact]
    public async Task TranslateScanned_UsesTheFoundEnglishCues()
    {
        var english = Cues(0, "English");
        var inner = new RecordingPipeline();
        var pipeline = new SubtitleAcquisitionPipeline(inner, new Downloader(language => language == SubtitleLanguage.English
            ? new DownloadedSubtitles(language, "NapiProjekt", english)
            : null));

        var scan = await pipeline.ScanAsync(Video, null);
        await pipeline.TranslateScannedAsync(scan, NullProvider(), exportTxt: false);

        Assert.Equal(Video, inner.VideoPath);
        Assert.Same(english, inner.Cues);
    }

    [Fact]
    public async Task TranslateScanned_WithoutEnglish_Throws()
    {
        var pipeline = Pipeline(new Downloader(_ => null));
        var scan = await pipeline.ScanAsync(Video, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.TranslateScannedAsync(scan, NullProvider(), exportTxt: false));
    }

    private static SubtitleAcquisitionPipeline Pipeline(ISubtitleDownloader downloader, IReadOnlyList<SubtitleCue>? embedded = null) =>
        new(new RecordingPipeline(), downloader, new CueReader(embedded ?? []), new SubtitleSynchronizationService());

    private static SubtitleCue[] Cues(double offsetSeconds, string text) =>
        Enumerable.Range(1, 12)
            .Select(index => new SubtitleCue(index,
                TimeSpan.FromSeconds(index * 10 + offsetSeconds),
                TimeSpan.FromSeconds(index * 10 + 1 + offsetSeconds),
                $"{text} {index}"))
            .ToArray();

    private sealed class Downloader(Func<SubtitleLanguage, DownloadedSubtitles?> get) : ISubtitleDownloader
    {
        public List<SubtitleLanguage> Calls { get; } = [];

        public Task<DownloadedSubtitles?> DownloadAsync(string videoPath, SubtitleLanguage language,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(language);
            return Task.FromResult(get(language));
        }
    }

    private sealed class CueReader(IReadOnlyList<SubtitleCue> embedded) : ISubtitleCueReader
    {
        public Task<IReadOnlyList<SubtitleCue>> ReadEmbeddedAsync(string videoPath, SubtitleTrack track,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) => Task.FromResult(embedded);

        public Task<IReadOnlyList<SubtitleCue>> ReadFileAsync(string subtitlePath,
            CancellationToken cancellationToken = default) => Task.FromResult(embedded);
    }

    private sealed class RecordingPipeline : IVideoSubtitleTranslationPipeline
    {
        public string? VideoPath { get; private set; }
        public IReadOnlyList<SubtitleCue>? Cues { get; private set; }

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
            IProgress<string>? status = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("A scan must not translate.");
    }

    private static ITranslationProvider NullProvider() =>
        new DeferredTranslationProvider(() => throw new InvalidOperationException("Not used."));
}
