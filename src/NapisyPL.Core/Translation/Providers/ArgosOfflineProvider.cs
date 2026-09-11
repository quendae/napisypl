using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class ArgosOfflineProvider(IArgosTranslatorClient client) : ITranslationProvider
{
    private static readonly HashSet<string> DotAbbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        "mr.", "mrs.", "ms.", "dr.", "prof.", "sr.", "jr.", "st.", "vs.", "etc.", "e.g.", "i.e."
    };

    public string DisplayName => "Argos EN→PL";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.MachineTranslation;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return new Dictionary<int, string>();

        var partsBySegment = segments
            .Select(segment => SplitForTranslation(segment.Text))
            .ToArray();
        var flatParts = partsBySegment.SelectMany(parts => parts).ToArray();

        var translatedParts = flatParts.Length == 0
            ? Array.Empty<string>()
            : (await client.TranslateAsync(flatParts, cancellationToken)).ToArray();

        if (translatedParts.Length != flatParts.Length)
            throw new InvalidDataException("Argos zwrócił inną liczbę segmentów niż wysłano.");

        var result = new Dictionary<int, string>(segments.Count);
        var offset = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var count = partsBySegment[i].Count;
            if (count == 0)
            {
                result[segments[i].Id] = string.Empty;
                continue;
            }

            result[segments[i].Id] = string.Join(" ", translatedParts.Skip(offset).Take(count))
                .Replace("  ", " ", StringComparison.Ordinal)
                .Trim();
            offset += count;
        }

        return result;
    }

    private static IReadOnlyList<string> SplitForTranslation(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var result = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

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
        return result;
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
