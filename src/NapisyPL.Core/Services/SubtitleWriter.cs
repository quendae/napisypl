using System.Text;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed class SubtitleWriter
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string BuildSrt(IEnumerable<SubtitleCue> cues)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var cue in cues)
        {
            builder.Append(index++).Append("\r\n")
                .Append(FormatTime(cue.Start)).Append(" --> ").Append(FormatTime(cue.End)).Append("\r\n")
                .Append(cue.Text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"))
                .Append("\r\n\r\n");
        }
        return builder.ToString();
    }

    public Task WriteSrtAsync(string path, IEnumerable<SubtitleCue> cues, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, BuildSrt(cues), Utf8NoBom, cancellationToken);

    public Task WriteTxtAsync(string path, IEnumerable<SubtitleCue> cues, CancellationToken cancellationToken = default)
    {
        var text = string.Join(Environment.NewLine + Environment.NewLine, cues.Select(c => c.Text));
        return File.WriteAllTextAsync(path, text, Utf8NoBom, cancellationToken);
    }

    public Task WriteTxtAsync(string path, IEnumerable<string> lines, CancellationToken cancellationToken = default) =>
        File.WriteAllTextAsync(path, string.Join(Environment.NewLine, lines), Utf8NoBom, cancellationToken);

    private static string FormatTime(TimeSpan time)
    {
        var hours = (int)time.TotalHours;
        return $"{hours:00}:{time.Minutes:00}:{time.Seconds:00},{time.Milliseconds:000}";
    }
}
