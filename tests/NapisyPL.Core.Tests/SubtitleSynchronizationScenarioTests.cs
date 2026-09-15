using System.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class SubtitleSynchronizationScenarioTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NapisyPL-sync-scenario-" + Guid.NewGuid().ToString("N"));

    public SubtitleSynchronizationScenarioTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task SuppliedCueShapeIsAlignedWithoutRewritingThePolishTimeline()
    {
        var reference = BuildReferenceTimeline();
        var candidate = BuildDownloadedTimeline(reference, includeCredits: true);
        var stopwatch = Stopwatch.StartNew();
        var analysis = new SubtitleSynchronizationService().Analyze(reference, candidate);
        stopwatch.Stop();

        Assert.Equal(802, reference.Length);
        Assert.Equal(768, candidate.Where(cue => !cue.Text.StartsWith("provider credit", StringComparison.Ordinal)).Count());
        Assert.Equal(2, candidate.Where(cue => cue.Text.StartsWith("provider credit", StringComparison.Ordinal)).Count());
        Assert.Equal(SubtitleSyncDecision.Aligned, analysis.Decision);
        Assert.InRange(analysis.Transform.Scale, 0.999, 1.001);
        Assert.InRange(analysis.Transform.Offset.TotalMilliseconds, -101, -99);
        Assert.True(analysis.P90Residual < TimeSpan.FromMilliseconds(350), analysis.ToString());
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"Synchronizing 802 reference and 768 downloaded cues took {stopwatch.Elapsed}.");

        var output = Path.Combine(_directory, "sample.pl.srt");
        var downloader = new FakeDownloader(candidate);
        var pipeline = new RecordingPipeline();
        var service = new SubtitleAcquisitionPipeline(
            pipeline,
            downloader,
            new FakeCueReader(reference),
            new SubtitleSynchronizationService());

        await service.TranslateAsync(
            Path.Combine(_directory, "sample.mkv"),
            new SubtitleTrack(1, "subrip", "eng", null, true),
            new DeferredTranslationProvider(() => throw new InvalidOperationException("translation must stay lazy")),
            exportTxt: false);

        var written = new SrtParser().Parse(File.ReadAllText(output));
        Assert.Equal(candidate.Select(cue => cue.Start), written.Select(cue => cue.Start));
        Assert.Equal(candidate.Select(cue => cue.End), written.Select(cue => cue.End));
        Assert.Equal(candidate.Select(cue => cue.Text), written.Select(cue => cue.Text));
        Assert.Empty(pipeline.TranslationEvents);
    }

    [Fact]
    public async Task MachineTranslationStartsOnlyAfterPolishCandidateIsRejected()
    {
        var reference = BuildReferenceTimeline().Take(12).ToArray();
        var rejected = reference
            .Select((cue, index) => new SubtitleCue(index + 1,
                cue.Start.Add(TimeSpan.FromMinutes(20)),
                cue.End.Add(TimeSpan.FromMinutes(20)),
                "rejected Polish"))
            .ToArray();
        var events = new List<string>();
        var pipeline = new RecordingPipeline(events);
        var downloader = new FakeDownloader(rejected, events);
        var statuses = new List<string>();
        var service = new SubtitleAcquisitionPipeline(
            pipeline,
            downloader,
            new FakeCueReader(reference),
            new SubtitleSynchronizationService());

        await service.TranslateAsync(
            Path.Combine(_directory, "rejected.mkv"),
            new SubtitleTrack(1, "subrip", "eng", null, true),
            new DeferredTranslationProvider(() => throw new InvalidOperationException("provider is not called by this fake")),
            exportTxt: false,
            status: new Progress<string>(statuses.Add));

        Assert.Equal([SubtitleLanguage.Polish], downloader.Calls);
        Assert.Same(reference, pipeline.TranslatedCues);
        Assert.Contains(statuses, message => message.Contains("Rejected", StringComparison.Ordinal));
        Assert.Equal(["download Polish", "translate embedded English"], events);
    }

    private static SubtitleCue[] BuildReferenceTimeline() => Enumerable.Range(0, 802)
        .Select(index => new SubtitleCue(index + 1,
            TimeSpan.FromSeconds(2 + index * 3),
            TimeSpan.FromSeconds(3 + index * 3),
            "reference cue " + (index + 1)))
        .ToArray();

    private static SubtitleCue[] BuildDownloadedTimeline(IReadOnlyList<SubtitleCue> reference, bool includeCredits)
    {
        var merged = Enumerable.Range(0, 20).Select(index => index * 40).ToHashSet();
        var omitted = reference.Select((_, index) => index)
            .Where(index => !merged.Contains(index) && !merged.Contains(index - 1))
            .Take(14)
            .ToHashSet();
        var candidate = new List<SubtitleCue>();
        for (var index = 0; index < reference.Count; index++)
        {
            if (merged.Contains(index) && index + 1 < reference.Count)
            {
                candidate.Add(new SubtitleCue(candidate.Count + 1,
                    reference[index].Start.Add(TimeSpan.FromMilliseconds(100)),
                    reference[index + 1].End.Add(TimeSpan.FromMilliseconds(100)),
                    "merged Polish cue " + (index + 1)));
                index++;
            }
            else if (!omitted.Contains(index))
            {
                candidate.Add(new SubtitleCue(candidate.Count + 1,
                    reference[index].Start.Add(TimeSpan.FromMilliseconds(100)),
                    reference[index].End.Add(TimeSpan.FromMilliseconds(100)),
                    "Polish cue " + (index + 1)));
            }
        }

        if (includeCredits)
        {
            candidate.Add(new SubtitleCue(candidate.Count + 1, TimeSpan.FromHours(1), TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(2)), "provider credit one"));
            candidate.Add(new SubtitleCue(candidate.Count + 1, TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(5)), TimeSpan.FromHours(1).Add(TimeSpan.FromSeconds(7)), "provider credit two"));
        }
        return candidate.ToArray();
    }

    private sealed class FakeCueReader(IReadOnlyList<SubtitleCue> embedded) : ISubtitleCueReader
    {
        public Task<IReadOnlyList<SubtitleCue>> ReadEmbeddedAsync(string videoPath, SubtitleTrack track,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) => Task.FromResult(embedded);

        public Task<IReadOnlyList<SubtitleCue>> ReadFileAsync(string subtitlePath,
            CancellationToken cancellationToken = default) => Task.FromResult(embedded);
    }

    private sealed class FakeDownloader(IReadOnlyList<SubtitleCue> cues, List<string>? events = null) : ISubtitleDownloader
    {
        public List<SubtitleLanguage> Calls { get; } = [];

        public Task<DownloadedSubtitles?> DownloadAsync(string videoPath, SubtitleLanguage language,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(language);
            events?.Add("download " + language);
            return Task.FromResult<DownloadedSubtitles?>(language == SubtitleLanguage.Polish
                ? new DownloadedSubtitles(language, "synthetic QNapi", cues)
                : null);
        }
    }

    private sealed class RecordingPipeline(List<string>? events = null) : IVideoSubtitleTranslationPipeline
    {
        public IReadOnlyList<SubtitleCue>? TranslatedCues { get; private set; }
        public List<string> TranslationEvents { get; } = [];

        public Task<TranslationResult> TranslateVideoSubtitlesAsync(string videoPath, IReadOnlyList<SubtitleCue> sourceCues,
            ITranslationProvider provider, bool exportTxt, IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            TranslatedCues = sourceCues;
            TranslationEvents.Add("translate");
            events?.Add("translate embedded English");
            return Task.FromResult(new TranslationResult(videoPath + ".pl.srt", null, sourceCues.Count));
        }

        public Task<TranslationResult> TranslateAsync(string inputPath, SubtitleTrack? selectedTrack,
            ITranslationProvider provider, bool exportTxt, IProgress<TranslationProgress>? translationProgress = null,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TranslationResult(inputPath + ".pl.srt", null, 0));
    }
}
