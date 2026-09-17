using System.Globalization;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerDiarizationOptions(
    string BaseDirectory,
    string SegmentationModelUrl,
    string EmbeddingModelUrl,
    double ClusterThreshold)
{
    public string SegmentationModelPath => Path.Combine(BaseDirectory, "segmentation.onnx");

    /// <summary>
    /// Named after the model file, so switching embedding models downloads the new
    /// one instead of silently reusing whatever "embedding.onnx" is already on disk.
    /// </summary>
    public string EmbeddingModelPath => Path.Combine(BaseDirectory, EmbeddingModelFileName);

    public string CacheSignature =>
        $"sherpa-pyannote3-{Path.GetFileNameWithoutExtension(EmbeddingModelFileName)}-v4-centre-cluster-" +
        ClusterThreshold.ToString("0.00", CultureInfo.InvariantCulture);

    private string EmbeddingModelFileName =>
        Uri.TryCreate(EmbeddingModelUrl, UriKind.Absolute, out var uri) &&
        Path.GetFileName(uri.AbsolutePath) is { Length: > 0 } name
            ? name
            : "embedding.onnx";

    public static SpeakerDiarizationOptions CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SpeakerDiarizationOptions(
            Path.Combine(localAppData, "SubFlow", "diarization"),
            "https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx?download=true",
            // WeSpeaker ResNet34, the embedding family pyannote Community-1 uses.
            // Measured on S01E08 (centre channel), with confident per-cue pitch as
            // a proxy label for clusters that mix a male and a female voice:
            //   CAM++    0.65 -> 64 speakers, 36% of gendered cues in the wrong cluster
            //   ResNet34 0.70 -> 12 speakers,  8%
            //   pyannote Community-1 (reference) -> 12 speakers, 13%, 4x slower
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/wespeaker_en_voxceleb_resnet34_LM.onnx",
            // Swept 0.50-0.80: 0.70 gives a realistic cast size while keeping mixing
            // low. CacheSignature embeds this value and the model, so changing
            // either invalidates existing caches.
            0.70);
    }
}
