using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerDiarizationOptionsTests
{
    [Fact]
    public void CreateDefault_UsesMeasuredResNet34Threshold()
    {
        var options = SpeakerDiarizationOptions.CreateDefault();

        Assert.Equal(0.70, options.ClusterThreshold, precision: 2);
    }

    [Fact]
    public void CacheSignature_ChangesWhenClusteringThresholdChanges()
    {
        var defaults = SpeakerDiarizationOptions.CreateDefault();
        var tuned = defaults with { ClusterThreshold = defaults.ClusterThreshold + 0.05 };

        Assert.NotEqual(defaults.CacheSignature, tuned.CacheSignature);
        Assert.Contains("cluster", defaults.CacheSignature, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmbeddingModelFileFollowsTheModelSoAStaleFileIsNeverReused()
    {
        var defaults = SpeakerDiarizationOptions.CreateDefault();
        var campplus = defaults with
        {
            EmbeddingModelUrl = "https://example.test/models/3dspeaker_speech_campplus_sv_en_voxceleb_16k.onnx"
        };

        Assert.EndsWith("wespeaker_en_voxceleb_resnet34_LM.onnx", defaults.EmbeddingModelPath);
        Assert.NotEqual(defaults.EmbeddingModelPath, campplus.EmbeddingModelPath);
        Assert.NotEqual(defaults.CacheSignature, campplus.CacheSignature);
    }
}
