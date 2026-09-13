using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public sealed record MachineTranslationPreparedBatch(
    IReadOnlyList<TranslationSegment> OriginalSegments,
    IReadOnlyList<int> PartCounts,
    IReadOnlyList<string> Parts);

public sealed class MachineTranslationTextPreprocessor
{
    private static readonly HashSet<string> DotAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr.", "mrs.", "ms.", "dr.", "prof.", "sr.", "jr.", "st.", "vs.", "etc.", "e.g.", "i.e."
    };

    public const int SchemaVersion = 1;

    public MachineTranslationPreparedBatch Prepare(IReadOnlyList<TranslationSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var originals = segments.ToArray();
        var partCounts = new int[originals.Length];
        var parts = new List<string>();

        for (var i = 0; i < originals.Length; i++)
        {
            var split = SplitForTranslation(originals[i].Text);
            partCounts[i] = split.Count;
            parts.AddRange(split);
        }

        return new MachineTranslationPreparedBatch(originals, partCounts, parts);
    }

    public IReadOnlyDictionary<int, string> Reassemble(
        MachineTranslationPreparedBatch batch,
        IReadOnlyList<string> translatedParts)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(translatedParts);

        if (translatedParts.Count != batch.Parts.Count)
            throw new InvalidDataException("Translator zwrócił inną liczbę segmentów niż wysłano.");

        if (batch.OriginalSegments.Count != batch.PartCounts.Count)
            throw new InvalidDataException("Prepared batch ma niespójną liczbę segmentów i part counts.");

        var result = new Dictionary<int, string>(batch.OriginalSegments.Count);
        var offset = 0;
        for (var i = 0; i < batch.OriginalSegments.Count; i++)
        {
            var count = batch.PartCounts[i];
            if (count < 0 || offset + count > translatedParts.Count)
                throw new InvalidDataException("Prepared batch zawiera nieprawidłowe part counts.");

            result[batch.OriginalSegments[i].Id] = count == 0
                ? string.Empty
                : string.Join(" ", translatedParts.Skip(offset).Take(count))
                    .Replace("  ", " ", StringComparison.Ordinal)
                    .Trim();
            offset += count;
        }

        if (offset != translatedParts.Count)
            throw new InvalidDataException("Prepared batch nie wykorzystał wszystkich przetłumaczonych części.");

        return result;
    }

    private static IReadOnlyList<string> SplitForTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var result = new List<string>();
        foreach (var logicalLine in BuildLogicalLines(text))
            SplitSentences(logicalLine, result);
        return result;
    }

    private static IReadOnlyList<string> BuildLogicalLines(string text)
    {
        var physicalLines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (physicalLines.Length == 0)
            return [];

        var logicalLines = new List<string>();
        var current = new List<string>();

        foreach (var line in physicalLines)
        {
            if (StartsDialogueTurn(line) && current.Count > 0)
            {
                logicalLines.Add(string.Join(" ", current));
                current.Clear();
            }

            current.Add(line);
        }

        if (current.Count > 0)
            logicalLines.Add(string.Join(" ", current));

        return logicalLines;
    }

    private static bool StartsDialogueTurn(string line)
    {
        var visible = line.TrimStart();
        while (visible.Length > 0)
        {
            if (visible[0] == '<')
            {
                var end = visible.IndexOf('>');
                if (end < 0)
                    break;
                visible = visible[(end + 1)..].TrimStart();
                continue;
            }

            if (visible[0] == '{')
            {
                var end = visible.IndexOf('}');
                if (end < 0)
                    break;
                visible = visible[(end + 1)..].TrimStart();
                continue;
            }

            break;
        }

        return visible.StartsWith("- ", StringComparison.Ordinal) ||
               visible.StartsWith("– ", StringComparison.Ordinal) ||
               visible.StartsWith("— ", StringComparison.Ordinal);
    }

    private static void SplitSentences(string line, List<string> result)
    {
        var start = 0;
        for (var i = 0; i < line.Length; i++)
        {
            var punctuation = line[i];
            if (punctuation is not ('.' or '!' or '?' or '…'))
                continue;

            if (punctuation == '.' && IsDotAbbreviation(line, start, i))
                continue;

            var next = i + 1;
            while (next < line.Length && char.IsWhiteSpace(line[next]))
                next++;
            if (next >= line.Length || next == i + 1)
                continue;

            var part = line[start..(i + 1)].Trim();
            if (part.Length > 0)
                result.Add(part);
            start = next;
            i = next - 1;
        }

        var tail = line[start..].Trim();
        if (tail.Length > 0)
            result.Add(tail);
    }

    private static bool IsDotAbbreviation(string line, int sentenceStart, int dotIndex)
    {
        var tokenStart = dotIndex - 1;
        while (tokenStart >= sentenceStart && !char.IsWhiteSpace(line[tokenStart]))
            tokenStart--;
        var token = line[(tokenStart + 1)..(dotIndex + 1)].Trim('"', '\'', '(', '[', '{');
        return DotAbbreviations.Contains(token);
    }
}
