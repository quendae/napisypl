using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public class AudioChannelPlanTests
{
    [Theory]
    [InlineData("5.1")]
    [InlineData("5.1(side)")]
    [InlineData("7.1")]
    [InlineData("FL+FR+FC+LFE+BL+BR")]
    public void LayoutsWithAFrontCentre_UseTheCentreChannel(string layout)
    {
        Assert.Equal(AudioChannelMode.Centre, AudioChannelPlan.Choose(layout));
    }

    [Theory]
    [InlineData("stereo")]
    [InlineData("mono")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("FL+FR+BL+BR")]
    public void LayoutsWithoutAFrontCentre_Downmix(string? layout)
    {
        Assert.Equal(AudioChannelMode.Downmix, AudioChannelPlan.Choose(layout));
    }

    [Fact]
    public void CentreFilterSelectsFrontCentreOnly()
    {
        Assert.Equal(["-af", "pan=mono|c0=FC"], AudioChannelPlan.FilterArguments(AudioChannelMode.Centre));
        Assert.Equal(["-ac", "1"], AudioChannelPlan.FilterArguments(AudioChannelMode.Downmix));
    }

    [Fact]
    public void ParsesTheProbedLayout()
    {
        Assert.Equal("5.1(side)", AudioChannelPlan.ParseProbedLayout("channel_layout=5.1(side)\r\n"));
        Assert.Null(AudioChannelPlan.ParseProbedLayout(""));
    }
}
