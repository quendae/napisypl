using System.IO.Compression;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Subtitles.Online;

/// <summary>Turns a downloaded file (plain SRT or a ZIP of them) into cues for one episode.</summary>
public static class SubtitlePayload
{
    private static readonly SrtParser Parser = new();

    public static IReadOnlyList<SubtitleCue> Read(byte[] bytes, ReleaseName video)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4)
            return ReadArchive(bytes, video);
        return ParseValid(SubtitleTextDecoder.Decode(bytes));
    }

    private static IReadOnlyList<SubtitleCue> ReadArchive(byte[] bytes, ReleaseName video)
    {
        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entries = archive.Entries
            .Where(entry => entry.FullName.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) && entry.Length > 0)
            .ToArray();
        if (entries.Length == 0)
            throw new InvalidDataException("Archiwum nie zawiera pliku SRT.");

        // Season packs: take the file for this episode.
        var chosen = entries.Length == 1
            ? entries[0]
            : entries
                .Select(entry => (Entry: entry, Name: ReleaseName.Parse(entry.Name)))
                .Where(item => !video.IsEpisode || !item.Name.IsEpisode || video.SameEpisodeAs(item.Name))
                .OrderByDescending(item => video.IsEpisode && item.Name.IsEpisode)
                .ThenByDescending(item => video.Similarity(item.Name))
                .Select(item => item.Entry)
                .FirstOrDefault()
              ?? throw new InvalidDataException("Archiwum nie zawiera napisów do tego odcinka.");

        using var stream = chosen.Open();
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ParseValid(SubtitleTextDecoder.Decode(memory.ToArray()));
    }

    private static IReadOnlyList<SubtitleCue> ParseValid(string text)
    {
        var cues = Parser.Parse(text);
        if (cues.Count == 0 || cues.Any(cue => cue.Start < TimeSpan.Zero || cue.End <= cue.Start))
            throw new InvalidDataException("Pobrany plik nie zawiera poprawnych napisów SRT.");
        return cues;
    }
}
