using SherpaOnnx;

namespace NapisyPL.Core.ContextResolution;

public sealed class SpeakerDiarizationService(
    SpeakerDiarizationAssetManager assetManager,
    SpeakerDiarizationOptions options)
{
    public async Task<IReadOnlyList<SpeakerSegment>> AnalyzeAsync(
        string wavePath,
        IProgress<double>? progress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        await assetManager.EnsureAvailableAsync(status, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        status?.Report("Enhanced: rozpoznaję, kto mówi w poszczególnych fragmentach…");
        var wave = await Task.Run(() => PcmWaveReader.ReadMono16(wavePath), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var threadCount = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = options.SegmentationModelPath;
        config.Segmentation.Pyannote.WindowShiftRatio = 0.1f;
        config.Segmentation.NumThreads = threadCount;
        config.Segmentation.Provider = "cpu";
        config.Embedding.Model = options.EmbeddingModelPath;
        config.Embedding.NumThreads = threadCount;
        config.Embedding.Provider = "cpu";
        config.Clustering.NumClusters = -1;
        config.Clustering.Threshold = (float)options.ClusterThreshold;

        return await Task.Run<IReadOnlyList<SpeakerSegment>>(() =>
        {
            using var diarizer = new OfflineSpeakerDiarization(config);
            if (diarizer.SampleRate != wave.SampleRate)
                throw new InvalidDataException($"Model diarization oczekuje {diarizer.SampleRate} Hz, audio ma {wave.SampleRate} Hz.");

            OfflineSpeakerDiarizationProgressCallback callback = (processed, total, _) =>
            {
                if (total > 0)
                    progress?.Report(Math.Clamp((double)processed / total, 0, 1));
                // sherpa-onnx currently ignores this return value, so cancellation is checked after native processing.
                return 0;
            };

            var segments = diarizer.ProcessWithCallback(wave.Samples, callback, IntPtr.Zero);
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(1);

            return segments
                .Where(segment => segment.End > segment.Start)
                .Select(segment => new SpeakerSegment(segment.Start, segment.End, segment.Speaker))
                .ToArray();
        }, CancellationToken.None);
    }
}
