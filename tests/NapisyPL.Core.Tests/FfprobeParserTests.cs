using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class FfprobeParserTests
{
    [Fact]
    public void Parse_RecognizesEnglishTextAndBitmapTracks()
    {
        const string json = """
        {
          "streams": [
            { "index": 2, "codec_name": "subrip", "tags": { "language": "eng", "title": "English" } },
            { "index": 5, "codec_name": "hdmv_pgs_subtitle", "tags": { "language": "pol" } }
          ]
        }
        """;

        var tracks = FfprobeParser.Parse(json);

        Assert.Equal(2, tracks.Count);
        Assert.True(tracks[0].IsText);
        Assert.Equal("eng", tracks[0].Language);
        Assert.False(tracks[1].IsText);
        Assert.Same(tracks[0], FfprobeParser.ChooseDefault(tracks));
    }

    [Fact]
    public void ChooseDefault_PrefersFirstTextTrackWhenLanguageIsMissing()
    {
        const string json = """
        {
          "streams": [
            { "index": 1, "codec_name": "hdmv_pgs_subtitle" },
            { "index": 3, "codec_name": "ass", "tags": { "title": "Signs" } }
          ]
        }
        """;

        var tracks = FfprobeParser.Parse(json);

        Assert.Equal(3, FfprobeParser.ChooseDefault(tracks)?.StreamIndex);
    }
}
