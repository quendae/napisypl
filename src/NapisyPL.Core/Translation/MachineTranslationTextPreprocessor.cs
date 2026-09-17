using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

/// <param name="LineGroups">
/// Per original segment, how many parts make up each output line. Parts within a
/// line are joined with a space, lines with a line break.
/// </param>
public sealed record MachineTranslationPreparedBatch(
    IReadOnlyList<TranslationSegment> OriginalSegments,
    IReadOnlyList<int> PartCounts,
    IReadOnlyList<string> Parts,
    IReadOnlyList<IReadOnlyList<int>> LineGroups);

/// <summary>
/// Shapes subtitle cues into what a sentence-level MT model can actually translate.
///
/// Cues used to go to the model whole, line breaks included, on the theory that
/// the cue is useful pronoun context. Measured on a real episode with MADLAD-400 3B
/// that theory did not hold:
///   - a raw line break made the model echo English and loop ("to look at youto
///     look at you"), exploding 5 of 20 cues even with beam search;
///   - of 180 multi-sentence cues, translating the cue whole kept every sentence
///     in only 3; one sentence per part kept every sentence in 159.
/// So line breaks are removed and each sentence is its own part. Two-speaker
/// dialogue cues keep one output line per speaker.
/// </summary>
public sealed partial class MachineTranslationTextPreprocessor
{
    public const int SchemaVersion = 3;

    private static readonly string[] Abbreviations =
    [
        "Mr.", "Mrs.", "Ms.", "Dr.", "St.", "Jr.", "Sr.", "Mt.", "vs.", "Lt.", "Sgt.", "Capt.", "Prof."
    ];

    public MachineTranslationPreparedBatch Prepare(IReadOnlyList<TranslationSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var originals = segments.ToArray();
        var partCounts = new int[originals.Length];
        var lineGroups = new IReadOnlyList<int>[originals.Length];
        var parts = new List<string>();

        for (var i = 0; i < originals.Length; i++)
        {
            var groups = new List<int>();
            foreach (var utterance in SplitUtterances(originals[i].Text))
            {
                var sentences = SplitSentences(utterance);
                parts.AddRange(sentences);
                groups.Add(sentences.Count);
            }

            lineGroups[i] = groups;
            partCounts[i] = groups.Sum();
        }

        return new MachineTranslationPreparedBatch(originals, partCounts, parts, lineGroups);
    }

    public IReadOnlyDictionary<int, string> Reassemble(
        MachineTranslationPreparedBatch batch,
        IReadOnlyList<string> translatedParts)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(translatedParts);

        if (translatedParts.Count != batch.Parts.Count)
            throw new InvalidDataException("Translator zwrócił inną liczbę segmentów niż wysłano.");

        if (batch.OriginalSegments.Count != batch.PartCounts.Count ||
            batch.OriginalSegments.Count != batch.LineGroups.Count)
            throw new InvalidDataException("Prepared batch ma niespójną liczbę segmentów i part counts.");

        var result = new Dictionary<int, string>(batch.OriginalSegments.Count);
        var offset = 0;
        for (var i = 0; i < batch.OriginalSegments.Count; i++)
        {
            var groups = batch.LineGroups[i];
            if (groups.Sum() != batch.PartCounts[i] || offset + batch.PartCounts[i] > translatedParts.Count)
                throw new InvalidDataException("Prepared batch zawiera nieprawidłowe part counts.");

            var lines = new List<string>(groups.Count);
            foreach (var count in groups)
            {
                if (count < 0)
                    throw new InvalidDataException("Prepared batch zawiera nieprawidłowe part counts.");

                lines.Add(string.Join(" ", translatedParts.Skip(offset).Take(count))
                    .Replace("  ", " ", StringComparison.Ordinal)
                    .Trim());
                offset += count;
            }

            result[batch.OriginalSegments[i].Id] = string.Join("\n", lines.Where(line => line.Length > 0));
        }

        if (offset != translatedParts.Count)
            throw new InvalidDataException("Prepared batch nie wykorzystał wszystkich przetłumaczonych części.");

        return result;
    }

    /// <summary>
    /// One utterance per speaker. A cue is a two-speaker exchange when it has two or
    /// more dialogue-dash lines, or when a later line starts with a dash and the first
    /// does not ("I'm not going along with it.\n- I wish you luck with that.", the
    /// style of Doc S02E19). Joining such a cue into one part made MADLAD drop the
    /// first speaker's sentence entirely. Any other cue is a single utterance wrapped
    /// for display, so its lines are joined with a space.
    /// </summary>
    private static IReadOnlyList<string> SplitUtterances(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();

        if (!IsDialogueExchange(lines))
            return [string.Join(" ", lines)];

        var utterances = new List<string>();
        foreach (var line in lines)
        {
            if (DialogueLineRegex().IsMatch(line) || utterances.Count == 0)
                utterances.Add(line);
            else
                utterances[^1] = utterances[^1] + " " + line;
        }

        return utterances;
    }

    public static bool IsDialogueExchange(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return IsDialogueExchange(text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray());
    }

    private static bool IsDialogueExchange(IReadOnlyList<string> lines)
    {
        var dashLines = lines.Count(line => DialogueLineRegex().IsMatch(line));
        return dashLines >= 2 ||
               (dashLines == 1 && lines.Count > 1 && !DialogueLineRegex().IsMatch(lines[0]));
    }

    private static IReadOnlyList<string> SplitSentences(string utterance)
    {
        var pieces = SentenceBoundaryRegex().Split(utterance)
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length > 0)
            .ToList();

        // Re-attach pieces that were split after an abbreviation ("Mr. Russell").
        var sentences = new List<string>(pieces.Count);
        foreach (var piece in pieces)
        {
            if (sentences.Count > 0 && EndsWithAbbreviation(sentences[^1]))
                sentences[^1] = sentences[^1] + " " + piece;
            else
                sentences.Add(piece);
        }

        return sentences;
    }

    private static bool EndsWithAbbreviation(string sentence)
    {
        foreach (var abbreviation in Abbreviations)
        {
            if (!sentence.EndsWith(abbreviation, StringComparison.Ordinal))
                continue;

            var start = sentence.Length - abbreviation.Length;
            if (start == 0 || !char.IsLetter(sentence[start - 1]))
                return true;
        }

        return false;
    }

    [GeneratedRegex(@"^(?:(?:<[^>]+>|\{[^}]+\})\s*)*[-–—]")]
    private static partial Regex DialogueLineRegex();

    // After ., ! or ? (optionally closed by a quote, bracket or tag) and before
    // something that starts a new sentence.
    [GeneratedRegex(@"(?<=[.!?…](?:[""'”’)\]]|</[a-z]+>)?)\s+(?=(?:<[a-z]+>)?[\p{Lu}""“(\[♪¿¡])")]
    private static partial Regex SentenceBoundaryRegex();
}
