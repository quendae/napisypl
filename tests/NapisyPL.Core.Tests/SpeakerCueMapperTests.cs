using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerCueMapperTests
{
    [Fact]
    public void Map_AssignsSpeakerWithLargestOverlap()
    {
        var cues = new[]
        {
            new SubtitleCue(1, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), "Hello"),
            new SubtitleCue(2, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(7), "Hi")
        };
        var speakers = new[]
        {
            new SpeakerSegment(0.5, 2.0, 0),
            new SpeakerSegment(2.0, 4.2, 1),
            new SpeakerSegment(4.2, 7.1, 0)
        };

        var mapped = SpeakerCueMapper.Map(cues, speakers);

        Assert.Equal("SPEAKER_01", mapped[1]);
        Assert.Equal("SPEAKER_00", mapped[2]);
    }

    [Fact]
    public void Map_ReturnsNullWhenThereIsNoMeaningfulOverlap()
    {
        var cues = new[]
        {
            new SubtitleCue(10, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12), "Caption")
        };
        var speakers = new[]
        {
            new SpeakerSegment(7, 8, 0)
        };

        var mapped = SpeakerCueMapper.Map(cues, speakers);

        Assert.Null(mapped[10]);
    }

    [Fact]
    public void Map_UsesStableZeroPaddedSpeakerIds()
    {
        var cues = new[]
        {
            new SubtitleCue(3, TimeSpan.Zero, TimeSpan.FromSeconds(2), "Test")
        };

        var mapped = SpeakerCueMapper.Map(cues, [new SpeakerSegment(0, 2, 7)]);

        Assert.Equal("SPEAKER_07", mapped[3]);
    }
}
