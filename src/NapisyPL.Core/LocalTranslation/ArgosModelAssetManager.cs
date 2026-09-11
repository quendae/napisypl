namespace NapisyPL.Core.LocalTranslation;

public sealed class ArgosModelAssetManager(HttpClient httpClient, ArgosRuntimeOptions options)
{
    public async Task<string> EnsureModelAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.ModelDirectory);
        if (File.Exists(options.ModelPath) && new FileInfo(options.ModelPath).Length > 0)
            return options.ModelPath;

        var partialPath = options.ModelPath + ".partial";
        try
        {
            status?.Report("Argos: pobieram lokalny model EN→PL…");
            using var response = await httpClient.GetAsync(
                options.ModelUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                useAsync: true))
            {
                var buffer = new byte[1024 * 128];
                long downloaded = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    status?.Report(FormatProgress(downloaded, total));
                }

                await output.FlushAsync(cancellationToken);
            }

            if (!File.Exists(partialPath) || new FileInfo(partialPath).Length == 0)
                throw new InvalidDataException("Pobrany model Argos jest pusty.");

            File.Move(partialPath, options.ModelPath, overwrite: true);
            status?.Report("Argos: model EN→PL gotowy.");
            return options.ModelPath;
        }
        catch
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
            throw;
        }
    }

    private static string FormatProgress(long downloaded, long? total)
    {
        static string Mb(long value) => $"{value / 1024d / 1024d:0.0} MB";
        if (total is > 0)
        {
            var percent = Math.Clamp(downloaded * 100d / total.Value, 0, 100);
            return $"Argos: pobieram model EN→PL — {percent:0}% · {Mb(downloaded)} / {Mb(total.Value)}";
        }
        return $"Argos: pobieram model EN→PL — {Mb(downloaded)}";
    }
}
