using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class SubtitleCueReaderTests
{
    [Fact]
    public async Task ReadEmbeddedAsyncDeletesItsTemporarySrtAfterParsing()
    {
        var extractor = new FakeCueExtractor("1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        var reader = new SubtitleCueReader(extractor, new SrtParser());

        var cues = await reader.ReadEmbeddedAsync("movie.mkv", new SubtitleTrack(1, "subrip", "eng", null, true));

        Assert.Single(cues);
        Assert.NotNull(extractor.OutputPath);
        Assert.False(File.Exists(extractor.OutputPath));
    }

    [Fact]
    public async Task ReadEmbeddedAsyncDeletesItsTemporarySrtWhenValidationFails()
    {
        var extractor = new FakeCueExtractor("not an SRT file");
        var reader = new SubtitleCueReader(extractor, new SrtParser());

        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadEmbeddedAsync(
            "movie.mkv", new SubtitleTrack(1, "subrip", "eng", null, true)));

        Assert.NotNull(extractor.OutputPath);
        Assert.False(File.Exists(extractor.OutputPath));
    }

    [Fact]
    public async Task ReadEmbeddedAsyncDeletesItsOwnedTemporarySrtWhenExtractionThrows()
    {
        var extractor = new FakeCueExtractor("partial", new OperationCanceledException());
        var reader = new SubtitleCueReader(extractor, new SrtParser());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadEmbeddedAsync(
            "movie.mkv", new SubtitleTrack(1, "subrip", "eng", null, true)));

        Assert.NotNull(extractor.OutputPath);
        Assert.False(File.Exists(extractor.OutputPath));
    }

    [Fact]
    public async Task ReadFileAsyncValidatesWithoutModifyingTheSelectedSrt()
    {
        var selectedSrt = await CreateTemporarySrtAsync("1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        try
        {
            var original = await File.ReadAllTextAsync(selectedSrt);
            var reader = new SubtitleCueReader(new FakeCueExtractor("unused"), new SrtParser());

            var cues = await reader.ReadFileAsync(selectedSrt);

            Assert.Single(cues);
            Assert.Equal(original, await File.ReadAllTextAsync(selectedSrt));
        }
        finally
        {
            if (File.Exists(selectedSrt)) File.Delete(selectedSrt);
        }
    }

    private static async Task<string> CreateTemporarySrtAsync(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), "NapisyPL-reader-" + Guid.NewGuid().ToString("N") + ".srt");
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private sealed class FakeCueExtractor(string content, Exception? failure = null) : ISubtitleCueExtractor
    {
        public string? OutputPath { get; private set; }

        public async Task ExtractToSrtAsync(string mediaPath, SubtitleTrack track, string outputPath,
            IProgress<string>? status = null, CancellationToken cancellationToken = default)
        {
            OutputPath = outputPath;
            await File.WriteAllTextAsync(outputPath, content, cancellationToken);
            if (failure is not null)
                throw failure;
        }
    }
}
