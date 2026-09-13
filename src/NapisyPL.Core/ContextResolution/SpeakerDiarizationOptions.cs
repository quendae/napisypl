namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerDiarizationOptions(
    string BaseDirectory,
    string SegmentationModelUrl,
    string EmbeddingModelUrl,
    double ClusterThreshold)
{
    public string SegmentationModelPath => Path.Combine(BaseDirectory, "segmentation.onnx");
    public string EmbeddingModelPath => Path.Combine(BaseDirectory, "embedding.onnx");

    public static SpeakerDiarizationOptions CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SpeakerDiarizationOptions(
            Path.Combine(localAppData, "SubFlow", "diarization"),
            "https://huggingface.co/csukuangfj/sherpa-onnx-pyannote-segmentation-3-0/resolve/main/model.onnx?download=true",
            "https://github.com/k2-fsa/sherpa-onnx/releases/download/speaker-recongition-models/3dspeaker_speech_campplus_sv_en_voxceleb_16k.onnx",
            0.65);
    }
}
