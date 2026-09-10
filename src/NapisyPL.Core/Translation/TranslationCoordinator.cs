using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public sealed class TranslationCoordinator
{
    private const int MaxSegmentsPerBatch = 40;
    private const int MaxCharactersPerBatch = 12000;

    public async Task<IReadOnlyList<SubtitleCue>> TranslateCuesAsync(
        IReadOnlyList<SubtitleCue> cues,
        ITranslationProvider provider,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
            return [];

        var translated = new List<SubtitleCue>(cues.Count);
        var completed = 0;
        foreach (var batch in BuildCueBatches(cues))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var segments = batch.Select(c => new TranslationSegment(c.Index, c.Text)).ToArray();
            var result = await provider.TranslateAsync(segments, cancellationToken);
            foreach (var cue in batch)
            {
                if (!result.TryGetValue(cue.Index, out var text))
                    throw new InvalidDataException($"Tłumacz nie zwrócił segmentu {cue.Index}.");
                translated.Add(cue with { Text = text });
                completed++;
            }
            progress?.Report((double)completed / cues.Count);
        }
        return translated;
    }

    public async Task<IReadOnlyList<string>> TranslateTextLinesAsync(
        IReadOnlyList<string> lines,
        ITranslationProvider provider,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var nonEmpty = lines.Select((text, index) => new { text, index }).Where(x => !string.IsNullOrWhiteSpace(x.text)).ToArray();
        if (nonEmpty.Length == 0)
            return lines;

        var output = lines.ToArray();
        var completed = 0;
        foreach (var batch in BuildTextBatches(nonEmpty.Select(x => new TranslationSegment(x.index + 1, x.text)).ToArray()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await provider.TranslateAsync(batch, cancellationToken);
            foreach (var segment in batch)
            {
                if (!result.TryGetValue(segment.Id, out var text))
                    throw new InvalidDataException($"Tłumacz nie zwrócił segmentu {segment.Id}.");
                output[segment.Id - 1] = text;
                completed++;
            }
            progress?.Report((double)completed / nonEmpty.Length);
        }
        return output;
    }

    private static IEnumerable<IReadOnlyList<SubtitleCue>> BuildCueBatches(IReadOnlyList<SubtitleCue> cues)
    {
        var batch = new List<SubtitleCue>();
        var characters = 0;
        foreach (var cue in cues)
        {
            if (batch.Count > 0 && (batch.Count >= MaxSegmentsPerBatch || characters + cue.Text.Length > MaxCharactersPerBatch))
            {
                yield return batch.ToArray();
                batch.Clear();
                characters = 0;
            }
            batch.Add(cue);
            characters += cue.Text.Length;
        }
        if (batch.Count > 0)
            yield return batch.ToArray();
    }

    private static IEnumerable<IReadOnlyList<TranslationSegment>> BuildTextBatches(IReadOnlyList<TranslationSegment> segments)
    {
        var batch = new List<TranslationSegment>();
        var characters = 0;
        foreach (var segment in segments)
        {
            if (batch.Count > 0 && (batch.Count >= MaxSegmentsPerBatch || characters + segment.Text.Length > MaxCharactersPerBatch))
            {
                yield return batch.ToArray();
                batch.Clear();
                characters = 0;
            }
            batch.Add(segment);
            characters += segment.Text.Length;
        }
        if (batch.Count > 0)
            yield return batch.ToArray();
    }
}
