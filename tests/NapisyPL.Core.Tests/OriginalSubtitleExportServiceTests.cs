using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class OriginalSubtitleExportServiceTests
{
    private static readonly SubtitleTrack TextTrack = new(2, "subrip", "eng", "English", true);

    [Fact]
    public async Task ExportAsync_DoesNotOverwriteTargetCreatedDuringExtraction()
    {
        using var fixture = new ExportFixture();
        var extractor = new DelegateExtractor(async (_, _, temporaryPath, _, cancellationToken) =>
        {
            await File.WriteAllTextAsync(temporaryPath, "extracted", cancellationToken);
            await File.WriteAllTextAsync(fixture.OutputPath, "existing", cancellationToken);
        });
        var service = new OriginalSubtitleExportService(extractor);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExportAsync(fixture.MediaPath, TextTrack, fixture.OutputPath));

        Assert.Contains("Movie.srt już istnieje", error.Message, StringComparison.Ordinal);
        Assert.Equal("existing", await File.ReadAllTextAsync(fixture.OutputPath));
        Assert.False(File.Exists(extractor.ObservedOutputPath));
    }

    [Fact]
    public async Task ExportAsync_CancellationLeavesNoPartialFinalOrTemporaryFile()
    {
        using var fixture = new ExportFixture();
        var extractor = new DelegateExtractor(async (_, _, temporaryPath, _, cancellationToken) =>
        {
            await File.WriteAllTextAsync(temporaryPath, "partial", cancellationToken);
            throw new OperationCanceledException(cancellationToken);
        });
        var service = new OriginalSubtitleExportService(extractor);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.ExportAsync(fixture.MediaPath, TextTrack, fixture.OutputPath));

        Assert.False(File.Exists(fixture.OutputPath));
        Assert.False(File.Exists(extractor.ObservedOutputPath));
    }

    private sealed class DelegateExtractor(
        Func<string, SubtitleTrack, string, IProgress<string>?, CancellationToken, Task> extract)
        : ISubtitleCueExtractor
    {
        public string? ObservedOutputPath { get; private set; }

        public Task ExtractToSrtAsync(
            string mediaPath,
            SubtitleTrack track,
            string outputPath,
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default)
        {
            ObservedOutputPath = outputPath;
            return extract(mediaPath, track, outputPath, status, cancellationToken);
        }
    }

    private sealed class ExportFixture : IDisposable
    {
        public ExportFixture()
        {
            Directory.CreateDirectory(Root);
            File.WriteAllBytes(MediaPath, [1]);
        }

        private string Root { get; } = Path.Combine(
            Path.GetTempPath(), "NapisyPL-OriginalExportServiceTests-" + Guid.NewGuid().ToString("N"));
        public string MediaPath => Path.Combine(Root, "Movie.mkv");
        public string OutputPath => Path.Combine(Root, "Movie.srt");

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
