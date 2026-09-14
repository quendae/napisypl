using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerDiarizationOptionsTests
{
    [Fact]
    public void CreateDefault_UsesConservativeUnknownSpeakerClusteringThreshold()
    {
        var options = SpeakerDiarizationOptions.CreateDefault();

        Assert.Equal(0.90, options.ClusterThreshold, precision: 2);
    }

    [Fact]
    public void CacheSignature_ChangesWhenClusteringThresholdChanges()
    {
        var defaults = SpeakerDiarizationOptions.CreateDefault();
        var tuned = defaults with { ClusterThreshold = defaults.ClusterThreshold - 0.05 };

        Assert.NotEqual(defaults.CacheSignature, tuned.CacheSignature);
        Assert.Contains("cluster", defaults.CacheSignature, StringComparison.OrdinalIgnoreCase);
    }
}
