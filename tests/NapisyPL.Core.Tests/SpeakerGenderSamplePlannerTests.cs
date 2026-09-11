using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerGenderSamplePlannerTests
{
    [Fact]
    public void SelectSegments_TakesThreeLongestUsefulSegmentsPerSpeaker()
    {
        SpeakerSegment[] segments =
        [
            new(0, 0.4, 0),
            new(1, 3.0, 0),
            new(4, 7.5, 0),
            new(8, 9.0, 0),
            new(10, 14.0, 0),
            new(15, 19.0, 1)
        ];

        var selected = SpeakerGenderSamplePlanner.SelectSegments(segments);

        Assert.Equal(3, selected[0].Count);
        Assert.DoesNotContain(selected[0], segment => segment.EndSeconds - segment.StartSeconds < 0.75);
        Assert.Equal(1, selected[1].Count);
        Assert.Equal(4.0, selected[1][0].EndSeconds - selected[1][0].StartSeconds, 3);
    }

    [Fact]
    public void CreateDefault_RequestsEnoughAudioTagsForGenderEvidence()
    {
        var options = SpeakerVoiceGenderOptions.CreateDefault();

        Assert.True(options.TopK >= 50);
    }
}
