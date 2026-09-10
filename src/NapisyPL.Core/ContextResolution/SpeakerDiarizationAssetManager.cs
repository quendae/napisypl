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
