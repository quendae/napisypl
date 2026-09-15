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
    public async Task SafeDownloadedPolishIsSynchronizedBeforeItIsWritten()
    {
        var reference = Cues(0, "English");
        var shiftedPolish = Cues(2, "Polish");
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(),
            new FakeDownloader(language => new(language, "QNapi", shiftedPolish)),
            new FakeCueReader(reference),
            new SubtitleSynchronizationService());

        await service.TranslateAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), NoTranslator(), false);

        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        Assert.Equal(reference.Select(cue => cue.Start), written.Select(cue => cue.Start));
        Assert.Equal(reference.Select(cue => cue.End), written.Select(cue => cue.End));
        Assert.Equal(shiftedPolish.Select(cue => cue.Text), written.Select(cue => cue.Text));
    }

    [Fact]
    public async Task AlignedDownloadedPolishKeepsItsOriginalTimes()
    {
        var reference = Cues(0, "English");
        var nearlyAlignedPolish = Cues(TimeSpan.FromMilliseconds(100), "Polish");
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(),
            new FakeDownloader(language => new(language, "QNapi", nearlyAlignedPolish)),
            new FakeCueReader(reference),
            new SubtitleSynchronizationService());

        await service.TranslateAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), NoTranslator(), false);

        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        Assert.Equal(nearlyAlignedPolish.Select(cue => cue.Start), written.Select(cue => cue.Start));
        Assert.Equal(nearlyAlignedPolish.Select(cue => cue.End), written.Select(cue => cue.End));
    }

    [Fact]
    public async Task UnsafeDownloadedPolishIsNotWrittenAndFallsBackToEmbeddedEnglish()
    {
        var reference = Cues(0, "English");
        var unsafePolish = new[]
        {
            new SubtitleCue(1, TimeSpan.FromHours(1), TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)), "Polish")
        };
        var inner = new FakePipeline();
        var downloader = new FakeDownloader(language => language == SubtitleLanguage.Polish ? new(language, "QNapi", unsafePolish) : null);
        var service = new SubtitleAcquisitionPipeline(
            inner,
            downloader,
            new FakeCueReader(reference),
            new SubtitleSynchronizationService());

        await service.TranslateAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), NoTranslator(), false);

        Assert.False(File.Exists(Output));
        Assert.Same(reference, inner.Cues);
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
    }

    [Fact]
    public async Task InteractiveLocalPolishIsSynchronizedBeforeItIsWritten()
    {
        var reference = Cues(0, "English");
        var localPolish = Cues(2, "Polish");
        var downloader = new FakeDownloader(_ => null);
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), downloader, new FakeCueReader(reference, localPolish), new SubtitleSynchronizationService());

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.UseLocalPolishSrt, "chosen.srt")),
            NoTranslator(), false);

        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        Assert.Equal(reference.Select(cue => cue.Start), written.Select(cue => cue.Start));
        Assert.Equal(localPolish.Select(cue => cue.Text), written.Select(cue => cue.Text));
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
    }

    [Fact]
    public async Task EmbeddedEnglishIsTranslatedBeforeDownloadingEnglish()
    {
        var reference = Cues(0, "English");
        var downloader = new FakeDownloader(language => language == SubtitleLanguage.Polish ? null : new(language, "QNapi", English));
        var inner = new FakePipeline();
        var service = new SubtitleAcquisitionPipeline(inner, downloader, new FakeCueReader(reference), new SubtitleSynchronizationService());

        await service.TranslateAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), NoTranslator(), false);

        Assert.Same(reference, inner.Cues);
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
    }

    [Fact]
    public async Task InteractiveUseWithoutChangesAcceptsOnlyTheReviewablePolishCandidate()
    {
        var reference = ExtendedCues(0, "English");
        var reviewablePolish = ReviewableCues(reference);
        Assert.Equal(SubtitleSyncDecision.NeedsReview,
            new SubtitleSynchronizationService().Analyze(reference, reviewablePolish).Decision);
        var downloader = new FakeDownloader(language => new(language, "QNapi", reviewablePolish));
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), downloader, new FakeCueReader(reference), new SubtitleSynchronizationService());

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.UseWithoutChanges)), NoTranslator(), false);

        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        Assert.Equal(reviewablePolish.Select(cue => cue.Start), written.Select(cue => cue.Start));
        Assert.Equal(reviewablePolish.Select(cue => cue.End), written.Select(cue => cue.End));
    }

    [Fact]
    public async Task InteractiveApplyRecommendedTransformAppliesOnlyTheReviewableAutomaticTransform()
    {
        var reference = ExtendedCues(0, "English");
        var reviewablePolish = ReviewableCues(reference);
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(language => new(language, "QNapi", reviewablePolish)),
            new FakeCueReader(reference), new SubtitleSynchronizationService());
        var analysis = new SubtitleSynchronizationService().Analyze(reference, reviewablePolish);
        Assert.Equal(SubtitleSyncDecision.NeedsReview, analysis.Decision);

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.ApplyRecommendedTransform)), NoTranslator(), false);

        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        var expected = new SubtitleSynchronizationService().Apply(reviewablePolish, analysis.Transform);
        Assert.Equal(expected.Select(cue => ToSrtPrecision(cue.Start)), written.Select(cue => cue.Start));
        Assert.Equal(expected.Select(cue => ToSrtPrecision(cue.End)), written.Select(cue => cue.End));
    }

    [Fact]
    public async Task InteractiveUseWithoutChangesRejectsAnUnsafeAutomaticCandidate()
    {
        var reference = Cues(0, "English");
        SubtitleCue[] unsafePolish = [new SubtitleCue(1, TimeSpan.FromHours(1), TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)), "Polish")];
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(language => new(language, "QNapi", unsafePolish)),
            new FakeCueReader(reference), new SubtitleSynchronizationService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TranslateInteractiveAsync(
            Video, new SubtitleTrack(4, "subrip", "eng", null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.UseWithoutChanges)), NoTranslator(), false));
        Assert.False(File.Exists(Output));
    }

    [Fact]
    public async Task InteractiveCancelAbortsInsteadOfContinuingToEnglish()
    {
        var reference = Cues(0, "English");
        var inner = new FakePipeline();
        var service = new SubtitleAcquisitionPipeline(
            inner, new FakeDownloader(_ => null), new FakeCueReader(reference), new SubtitleSynchronizationService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranslateInteractiveAsync(
            Video, new SubtitleTrack(4, "subrip", "eng", null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel)), NoTranslator(), false));
        Assert.Null(inner.Cues);
    }

    [Fact]
    public async Task InteractiveQnapiChoiceIsAnalyzedBeforeItsPolishCandidateIsWritten()
    {
        var reference = Cues(0, "English");
        var shiftedPolish = Cues(2, "Polish");
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(_ => null), new FakeCueReader(reference), new SubtitleSynchronizationService());

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.DownloadInteractivePolish, null,
                new DownloadedSubtitles(SubtitleLanguage.Polish, "QNapi selection", shiftedPolish))), NoTranslator(), false);

        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        Assert.Equal(reference.Select(cue => cue.Start), written.Select(cue => cue.Start));
        Assert.Equal(shiftedPolish.Select(cue => cue.Text), written.Select(cue => cue.Text));
    }

    [Fact]
    public async Task ExplicitEmbeddedEnglishChoiceFailsWhenNoEmbeddedCuesExist()
    {
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(language => language == SubtitleLanguage.Polish ? null : new(language, "QNapi", English)),
            new FakeCueReader(English), new SubtitleSynchronizationService());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.TranslateInteractiveAsync(
            Video, null, new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.TranslateEmbeddedEnglish)),
            NoTranslator(), false));
    }

    [Fact]
    public async Task ExplicitEmbeddedEnglishChoiceTranslatesAnUntaggedEmbeddedTrack()
    {
        var reference = Cues(0, "English");
        var inner = new FakePipeline();
        var downloader = new FakeDownloader(_ => null);
        var service = new SubtitleAcquisitionPipeline(
            inner, downloader, new FakeCueReader(reference), new SubtitleSynchronizationService());

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", null, null, true),
            new FakeFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.TranslateEmbeddedEnglish)), NoTranslator(), false);

        Assert.Same(reference, inner.Cues);
        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
    }

    [Fact]
    public async Task InteractiveLocalReviewCandidateIsPresentedAgainAndCanBeAcceptedWithoutChanges()
    {
        var reference = ExtendedCues(0, "English");
        var localPolish = ReviewableCues(reference);
        var fallback = new SequenceFallback(
            new SubtitleFallbackChoice(SubtitleFallbackAction.UseLocalPolishSrt, "chosen.srt"),
            new SubtitleFallbackChoice(SubtitleFallbackAction.UseWithoutChanges));
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(_ => null), new FakeCueReader(reference, localPolish), new SubtitleSynchronizationService());

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), fallback, NoTranslator(), false);

        Assert.Equal(2, fallback.Requests.Count);
        Assert.Same(localPolish, fallback.Requests[1].PolishCandidate!.Cues);
        Assert.Equal(SubtitleSyncDecision.NeedsReview, fallback.Requests[1].TimingAnalysis!.Decision);
        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        Assert.Equal(localPolish.Select(cue => cue.Start), written.Select(cue => cue.Start));
    }

    [Fact]
    public async Task InteractiveRejectedLocalCandidateRequiresAnExplicitFollowUpChoice()
    {
        var reference = Cues(0, "English");
        SubtitleCue[] rejected = [new SubtitleCue(1, TimeSpan.FromHours(1), TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(1)), "Polish")];
        var fallback = new SequenceFallback(
            new SubtitleFallbackChoice(SubtitleFallbackAction.UseLocalPolishSrt, "chosen.srt"),
            new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
        var inner = new FakePipeline();
        var service = new SubtitleAcquisitionPipeline(
            inner, new FakeDownloader(_ => null), new FakeCueReader(reference, rejected), new SubtitleSynchronizationService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranslateInteractiveAsync(
            Video, new SubtitleTrack(4, "subrip", "eng", null, true), fallback, NoTranslator(), false));

        Assert.Equal(2, fallback.Requests.Count);
        Assert.Equal(SubtitleSyncDecision.Rejected, fallback.Requests[1].TimingAnalysis!.Decision);
        Assert.Null(inner.Cues);
    }

    [Fact]
    public async Task InteractiveInvalidLocalCandidateRePromptsForAnExplicitFollowUpChoice()
    {
        var fallback = new SequenceFallback(
            new SubtitleFallbackChoice(SubtitleFallbackAction.UseLocalPolishSrt, "broken.srt"),
            new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(_ => null),
            new FakeCueReader(Cues(0, "English"), readFileException: new InvalidDataException("invalid local SRT")),
            new SubtitleSynchronizationService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranslateInteractiveAsync(
            Video, new SubtitleTrack(4, "subrip", "eng", null, true), fallback, NoTranslator(), false));

        Assert.Equal(2, fallback.Requests.Count);
    }

    [Fact]
    public async Task InteractiveQnapiReviewCandidateIsPresentedAgainAndCanUseItsRecommendedTransform()
    {
        var reference = ExtendedCues(0, "English");
        var reviewablePolish = ReviewableCues(reference);
        var fallback = new SequenceFallback(
            new SubtitleFallbackChoice(SubtitleFallbackAction.DownloadInteractivePolish, null,
                new DownloadedSubtitles(SubtitleLanguage.Polish, "QNapi selection", reviewablePolish)),
            new SubtitleFallbackChoice(SubtitleFallbackAction.ApplyRecommendedTransform));
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(_ => null), new FakeCueReader(reference), new SubtitleSynchronizationService());

        await service.TranslateInteractiveAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), fallback, NoTranslator(), false);

        Assert.Equal(2, fallback.Requests.Count);
        Assert.Same(reviewablePolish, fallback.Requests[1].PolishCandidate!.Cues);
        var analysis = fallback.Requests[1].TimingAnalysis!;
        Assert.Equal(SubtitleSyncDecision.NeedsReview, analysis.Decision);
        var written = new SrtParser().Parse(await File.ReadAllTextAsync(Output));
        var expected = new SubtitleSynchronizationService().Apply(reviewablePolish, analysis.Transform);
        Assert.Equal(expected.Select(cue => ToSrtPrecision(cue.Start)), written.Select(cue => cue.Start));
    }

    [Fact]
    public async Task EmbeddedExtractionInvalidOperationContinuesToDownloadedEnglish()
    {
        var messages = new List<string>();
        var inner = new FakePipeline();
        var downloader = new FakeDownloader(language => language == SubtitleLanguage.Polish ? null : new(language, "QNapi", English));
        var service = new SubtitleAcquisitionPipeline(
            inner, downloader, new ThrowingCueReader(new InvalidOperationException("ffmpeg unavailable")), new SubtitleSynchronizationService());

        await service.TranslateAsync(Video, new SubtitleTrack(4, "subrip", "eng", null, true), NoTranslator(), false,
            status: new InlineProgress<string>(messages.Add));

        Assert.Equal([SubtitleLanguage.Polish, SubtitleLanguage.English], downloader.Calls);
        Assert.Same(English, inner.Cues);
        Assert.Contains(messages, message => message.Contains("ffmpeg unavailable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FallbackRequestAdvertisesUntaggedEmbeddedTextSeparatelyFromAutomaticEnglish()
    {
        var fallback = new SequenceFallback(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
        var service = new SubtitleAcquisitionPipeline(
            new FakePipeline(), new FakeDownloader(_ => null), new FakeCueReader(Cues(0, "English")), new SubtitleSynchronizationService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TranslateInteractiveAsync(
            Video, new SubtitleTrack(4, "subrip", null, null, true), fallback, NoTranslator(), false));

        Assert.True(fallback.Requests.Single().HasEmbeddedTextTrack);
        Assert.False(fallback.Requests.Single().HasAutomaticallyTranslatableEmbeddedEnglish);
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
    private static SubtitleCue[] Cues(TimeSpan offset, string text) =>
    [
        new SubtitleCue(1, TimeSpan.FromSeconds(10).Add(offset), TimeSpan.FromSeconds(11).Add(offset), text + " 1"),
        new SubtitleCue(2, TimeSpan.FromSeconds(20).Add(offset), TimeSpan.FromSeconds(21).Add(offset), text + " 2"),
        new SubtitleCue(3, TimeSpan.FromSeconds(30).Add(offset), TimeSpan.FromSeconds(31).Add(offset), text + " 3"),
        new SubtitleCue(4, TimeSpan.FromSeconds(40).Add(offset), TimeSpan.FromSeconds(41).Add(offset), text + " 4"),
        new SubtitleCue(5, TimeSpan.FromSeconds(50).Add(offset), TimeSpan.FromSeconds(51).Add(offset), text + " 5")
    ];
    private static SubtitleCue[] Cues(int offsetSeconds, string text) => Cues(TimeSpan.FromSeconds(offsetSeconds), text);
    private static SubtitleCue[] ExtendedCues(int offsetSeconds, string text) =>
        Cues(offsetSeconds, text).Concat(
        [
            new SubtitleCue(6, TimeSpan.FromSeconds(60 + offsetSeconds), TimeSpan.FromSeconds(61 + offsetSeconds), text + " 6"),
            new SubtitleCue(7, TimeSpan.FromSeconds(70 + offsetSeconds), TimeSpan.FromSeconds(71 + offsetSeconds), text + " 7"),
            new SubtitleCue(8, TimeSpan.FromSeconds(80 + offsetSeconds), TimeSpan.FromSeconds(81 + offsetSeconds), text + " 8")
        ]).ToArray();
    private static SubtitleCue[] ReviewableCues(IReadOnlyList<SubtitleCue> reference) => reference
        .Select((cue, position) => new SubtitleCue(cue.Index,
            cue.Start.Add(TimeSpan.FromMilliseconds(position % 2 == 0 ? 0 : 900)),
            cue.End.Add(TimeSpan.FromMilliseconds(position % 2 == 0 ? 0 : 900)), "Polish " + cue.Index))
        .ToArray();
    private static TimeSpan ToSrtPrecision(TimeSpan time) => TimeSpan.FromMilliseconds((long)time.TotalMilliseconds);
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
    private sealed class FakeCueReader(
        IReadOnlyList<SubtitleCue> embedded,
        IReadOnlyList<SubtitleCue>? file = null,
        Exception? readFileException = null) : ISubtitleCueReader
    {
        public Task<IReadOnlyList<SubtitleCue>> ReadEmbeddedAsync(string videoPath, SubtitleTrack track,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) => Task.FromResult(embedded);

        public Task<IReadOnlyList<SubtitleCue>> ReadFileAsync(string subtitlePath,
            CancellationToken cancellationToken = default) => readFileException is null
                ? Task.FromResult(file ?? embedded)
                : Task.FromException<IReadOnlyList<SubtitleCue>>(readFileException);
    }
    private sealed class FakeFallback(SubtitleFallbackChoice choice) : ISubtitleFallbackInteraction
    {
        public Task<SubtitleFallbackChoice> ChooseAsync(SubtitleFallbackRequest request,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) => Task.FromResult(choice);
    }
    private sealed class SequenceFallback(params SubtitleFallbackChoice[] choices) : ISubtitleFallbackInteraction
    {
        private readonly Queue<SubtitleFallbackChoice> _choices = new(choices);
        public List<SubtitleFallbackRequest> Requests { get; } = [];

        public Task<SubtitleFallbackChoice> ChooseAsync(SubtitleFallbackRequest request,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(_choices.Dequeue());
        }
    }
    private sealed class ThrowingCueReader(Exception exception) : ISubtitleCueReader
    {
        public Task<IReadOnlyList<SubtitleCue>> ReadEmbeddedAsync(string videoPath, SubtitleTrack track,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<SubtitleCue>>(exception);

        public Task<IReadOnlyList<SubtitleCue>> ReadFileAsync(string subtitlePath,
            CancellationToken cancellationToken = default) => Task.FromException<IReadOnlyList<SubtitleCue>>(exception);
    }
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
