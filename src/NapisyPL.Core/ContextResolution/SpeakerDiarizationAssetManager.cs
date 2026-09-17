namespace NapisyPL.Core.ContextResolution;

public sealed class SpeakerDiarizationAssetManager(
    HttpClient httpClient,
    SpeakerDiarizationOptions options)
{
    public async Task EnsureAvailableAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.BaseDirectory);

        if (!File.Exists(options.SegmentationModelPath))
        {
            status?.Report("Enhanced: pobieram model segmentacji mówców (~6 MB)…");
            await DownloadAtomicAsync(options.SegmentationModelUrl, options.SegmentationModelPath, cancellationToken);
        }

        if (!File.Exists(options.EmbeddingModelPath))
        {
            status?.Report("Enhanced: pobieram model rozpoznawania mówców (~30 MB)…");
            await DownloadAtomicAsync(options.EmbeddingModelUrl, options.EmbeddingModelPath, cancellationToken);
        }
    }

    public const string SileroVadUrl = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx";

    public string SileroVadModelPath => Path.Combine(options.BaseDirectory, "silero_vad.onnx");

    /// <summary>The speech detector used to time subtitles of another release (~0.6 MB).</summary>
    public async Task<string> EnsureSileroVadAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.BaseDirectory);
        if (!File.Exists(SileroVadModelPath))
        {
            status?.Report("Pobieram model wykrywania mowy (~1 MB)…");
            await DownloadAtomicAsync(SileroVadUrl, SileroVadModelPath, cancellationToken);
        }
        return SileroVadModelPath;
    }

    private async Task DownloadAtomicAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var partial = destination + ".partial";
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);

            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }

            // Windows cannot rename a file opened with FileShare.None. The stream must be disposed first.
            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                try { File.Delete(partial); } catch { }
            }
        }
    }
}
