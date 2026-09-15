using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class SubtitleCueReaderTests
{
    [Fact]
    public async Task ReadEmbeddedAsyncDeletesItsTemporarySrtAfterParsing()
    {
        var temporarySrt = await CreateTemporarySrtAsync("1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        try
        {
            var reader = new SubtitleCueReader(new FakeCueExtractor(temporarySrt), new SrtParser());

            var cues = await reader.ReadEmbeddedAsync("movie.mkv", new SubtitleTrack(1, "subrip", "eng", null, true));

            Assert.Single(cues);
            Assert.False(File.Exists(temporarySrt));
        }
        finally
        {
            if (File.Exists(temporarySrt)) File.Delete(temporarySrt);
        }
    }

    [Fact]
    public async Task ReadEmbeddedAsyncDeletesItsTemporarySrtWhenValidationFails()
    {
        var temporarySrt = await CreateTemporarySrtAsync("not an SRT file");
        try
        {
            var reader = new SubtitleCueReader(new FakeCueExtractor(temporarySrt), new SrtParser());

            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadEmbeddedAsync(
                "movie.mkv", new SubtitleTrack(1, "subrip", "eng", null, true)));

            Assert.False(File.Exists(temporarySrt));
        }
        finally
        {
            if (File.Exists(temporarySrt)) File.Delete(temporarySrt);
        }
    }

    [Fact]
    public async Task ReadFileAsyncValidatesWithoutModifyingTheSelectedSrt()
    {
        var selectedSrt = await CreateTemporarySrtAsync("1\n00:00:01,000 --> 00:00:02,000\nHello\n");
        try
        {
            var original = await File.ReadAllTextAsync(selectedSrt);
            var reader = new SubtitleCueReader(new FakeCueExtractor("unused.srt"), new SrtParser());

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

    private sealed class FakeCueExtractor(string temporarySrt) : ISubtitleCueExtractor
    {
        public Task<string> ExtractToTemporarySrtAsync(string mediaPath, SubtitleTrack track,
            IProgress<string>? status = null, CancellationToken cancellationToken = default) => Task.FromResult(temporarySrt);
    }
}
