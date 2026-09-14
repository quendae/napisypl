using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class OriginalSubtitleExportTests
{
    [Theory]
    [InlineData(@"C:\Media\Movie.mkv", @"C:\Media\Movie.original.srt")]
    [InlineData(@"D:\TV\Better.Call.Saul.S01E08.1080p.x264.EAC3-SURGE.mp4", @"D:\TV\Better.Call.Saul.S01E08.1080p.x264.EAC3-SURGE.original.srt")]
    public void BuildOutputPath_PlacesOriginalSrtNextToVideo(string mediaPath, string expected)
    {
        Assert.Equal(expected, OriginalSubtitleExportPath.Build(mediaPath));
    }

    [Fact]
    public void BuildOutputPath_RejectsMissingMediaPath()
    {
        Assert.Throws<ArgumentException>(() => OriginalSubtitleExportPath.Build(" "));
    }
}
