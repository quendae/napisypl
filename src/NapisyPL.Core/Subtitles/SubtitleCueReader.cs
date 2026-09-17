using System.ComponentModel;
using System.Text;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Subtitles;

public sealed class SubtitleCueReader(ISubtitleCueExtractor extractionService, SrtParser parser) : ISubtitleCueReader
{
    public async Task<IReadOnlyList<SubtitleCue>> ReadEmbeddedAsync(
        string videoPath,
        SubtitleTrack track,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(videoPath);
        ArgumentNullException.ThrowIfNull(track);

        var temporarySrt = Path.Combine(Path.GetTempPath(), "NapisyPL-" + Guid.NewGuid().ToString("N") + ".srt");
        try
        {
            await extractionService.ExtractToSrtAsync(videoPath, track, temporarySrt, status, cancellationToken);
            return await ReadAndValidateAsync(temporarySrt, cancellationToken);
        }
        finally
        {
            try { File.Delete(temporarySrt); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<IReadOnlyList<SubtitleCue>> ReadFileAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subtitlePath);
        return await ReadAndValidateAsync(subtitlePath, cancellationToken);
    }

    private async Task<IReadOnlyList<SubtitleCue>> ReadAndValidateAsync(string path, CancellationToken cancellationToken)
    {
        var cues = parser.Parse(await SubtitleTextDecoder.ReadFileAsync(path, cancellationToken));
        if (!AreValid(cues))
            throw new InvalidDataException("Plik napisów jest pusty lub ma nieprawidłowe czasy/numery kwestii.");
        return cues;
    }

    private static bool AreValid(IReadOnlyList<SubtitleCue> cues) =>
        cues.Count > 0 &&
        cues.All(cue => cue.Start >= TimeSpan.Zero && cue.End > cue.Start && !string.IsNullOrWhiteSpace(cue.Text)) &&
        cues.Select(cue => cue.Index).Distinct().Count() == cues.Count;
}
