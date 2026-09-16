using NapisyPL.Core.Models;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Services;

public sealed class OriginalSubtitleExportService(ISubtitleCueExtractor extractor)
{
    public async Task ExportAsync(
        string mediaPath,
        SubtitleTrack track,
        string outputPath,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputPath))
            throw TargetAlreadyExists(outputPath);

        var directory = Path.GetDirectoryName(outputPath) ?? string.Empty;
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.NapisyPL-{Guid.NewGuid():N}.srt");

        try
        {
            await extractor.ExtractToSrtAsync(
                mediaPath, track, temporaryPath, status, cancellationToken);
            try
            {
                File.Move(temporaryPath, outputPath, overwrite: false);
            }
            catch (IOException) when (File.Exists(outputPath))
            {
                throw TargetAlreadyExists(outputPath);
            }
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static InvalidOperationException TargetAlreadyExists(string outputPath) =>
        new($"Plik {Path.GetFileName(outputPath)} już istnieje. " +
            "Przenieś go lub zmień jego nazwę przed wyciągnięciem oryginalnych napisów.");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup of the uniquely owned temporary file must not hide the operation result.
        }
    }
}
