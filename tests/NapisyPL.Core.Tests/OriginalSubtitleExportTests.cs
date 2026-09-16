using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class OriginalSubtitleExportTests
{
    [Theory]
    [InlineData(@"C:\Media\Movie.mkv", @"C:\Media\Movie.srt")]
    [InlineData(@"Z:\TV\Vice.Principals.S01E01.The.Principal.1080p.AMZN.WEB-DL.DD.5.1.H.265-SiGMA.mkv", @"Z:\TV\Vice.Principals.S01E01.The.Principal.1080p.AMZN.WEB-DL.DD.5.1.H.265-SiGMA.srt")]
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
