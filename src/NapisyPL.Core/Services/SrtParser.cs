using System.Globalization;
using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed partial class SrtParser
{
    private static readonly string[] TimeFormats = [@"hh\:mm\:ss\,fff", @"hh\:mm\:ss\.fff"];

    public IReadOnlyList<SubtitleCue> Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return [];

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n', '\uFEFF');
        var blocks = BlockSeparatorRegex().Split(normalized);
        var cues = new List<SubtitleCue>(blocks.Length);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n');
            if (lines.Length < 2)
                continue;

            var timingLineIndex = lines[0].Contains("-->", StringComparison.Ordinal) ? 0 : 1;
            if (timingLineIndex >= lines.Length || !TryParseTiming(lines[timingLineIndex], out var start, out var end))
                continue;

            var index = timingLineIndex == 1 && int.TryParse(lines[0].Trim(), out var parsedIndex)
                ? parsedIndex
                : cues.Count + 1;
            var text = string.Join("\n", lines.Skip(timingLineIndex + 1)).TrimEnd();
            if (text.Length == 0)
                continue;

            cues.Add(new SubtitleCue(index, start, end, text));
        }

        return cues;
    }

    private static bool TryParseTiming(string line, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;
        var parts = line.Split("-->", StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            return false;

        var endToken = parts[1].Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return TimeSpan.TryParseExact(parts[0], TimeFormats, CultureInfo.InvariantCulture, out start)
            && TimeSpan.TryParseExact(endToken, TimeFormats, CultureInfo.InvariantCulture, out end);
    }

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlockSeparatorRegex();
}
