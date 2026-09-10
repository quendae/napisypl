using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class SrtParserTests
{
    [Fact]
    public void Parse_ReadsMultilineCueAndTimings()
    {
        const string srt = "1\r\n00:00:01,250 --> 00:00:03,500\r\nHello there.\r\nHow are you?\r\n\r\n2\r\n00:00:04,000 --> 00:00:05,000\r\nGoodbye.\r\n";
        var cues = new SrtParser().Parse(srt);

        Assert.Equal(2, cues.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1250), cues[0].Start);
        Assert.Equal(TimeSpan.FromMilliseconds(3500), cues[0].End);
        Assert.Equal("Hello there.\nHow are you?", cues[0].Text);
    }

    [Fact]
    public void BuildSrt_UsesStableIndexesAndUtf8FriendlyText()
    {
        var cues = new[]
        {
            new SubtitleCue(7, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(2500), "Zażółć gęślą jaźń")
        };

        var output = new SubtitleWriter().BuildSrt(cues);

        Assert.Contains("1\r\n00:00:01,000 --> 00:00:02,500\r\nZażółć gęślą jaźń", output);
    }
}
