using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public sealed record MachineTranslationPreparedBatch(
    IReadOnlyList<TranslationSegment> OriginalSegments,
    IReadOnlyList<int> PartCounts,
    IReadOnlyList<string> Parts);

public sealed class MachineTranslationTextPreprocessor
{
    public const int SchemaVersion = 1;

    public MachineTranslationPreparedBatch Prepare(IReadOnlyList<TranslationSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var originals = segments.ToArray();
        var partCounts = new int[originals.Length];
        var parts = new List<string>();

        for (var i = 0; i < originals.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(originals[i].Text))
                continue;

            // A cue is the model's translation context. Keep all sentences and
            // dialogue lines together so the model can resolve pronouns and
            // preserve the cue's original line structure.
            partCounts[i] = 1;
            parts.Add(originals[i].Text);
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

}
