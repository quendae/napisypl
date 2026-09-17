using NapisyPL.Core.Subtitles;
using SherpaOnnx;

namespace NapisyPL.Core.ContextResolution;

/// <summary>Silero VAD through sherpa-onnx: when people speak, well enough to time subtitles by it.</summary>
public static class SileroSpeechDetector
{
    private const int WindowSize = 512;

    public static IReadOnlyList<SpeechSpan> Detect(PcmWaveData wave, string modelPath, float minimumSilenceSeconds = 0.1f)
    {
        ArgumentNullException.ThrowIfNull(wave);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        SherpaOnnxNative.EnsureLoaded();

        var config = new VadModelConfig();
        config.SileroVad.Model = modelPath;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = minimumSilenceSeconds;
        config.SileroVad.MinSpeechDuration = 0.25f;
        config.SileroVad.MaxSpeechDuration = 20f;
        config.SileroVad.WindowSize = WindowSize;
        config.SampleRate = wave.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";

        var spans = new List<SpeechSpan>();
        using var vad = new VoiceActivityDetector(config, 60);
        var chunk = new float[WindowSize];
        for (var offset = 0; offset + WindowSize <= wave.Samples.Length; offset += WindowSize)
        {
            Array.Copy(wave.Samples, offset, chunk, 0, WindowSize);
            vad.AcceptWaveform(chunk);
            Drain(vad, wave.SampleRate, spans);
        }

        vad.Flush();
        Drain(vad, wave.SampleRate, spans);
        return spans;
    }

    private static void Drain(VoiceActivityDetector vad, int sampleRate, List<SpeechSpan> spans)
    {
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            var start = TimeSpan.FromSeconds(segment.Start / (double)sampleRate);
            spans.Add(new SpeechSpan(start, start + TimeSpan.FromSeconds(segment.Samples.Length / (double)sampleRate)));
            vad.Pop();
        }
    }
}
