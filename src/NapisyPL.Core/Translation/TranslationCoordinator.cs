using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public sealed class TranslationCoordinator
{
    public Task<IReadOnlyList<SubtitleCue>> TranslateCuesAsync(
        IReadOnlyList<SubtitleCue> cues,
        ITranslationProvider provider,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<TranslationProgress>? structuredProgress = progress is null
            ? null
            : new InlineProgress<TranslationProgress>(value =>
            {
                if (!value.WaitingForProvider && value.TotalSegments > 0)
                    progress.Report((double)value.CompletedSegments / value.TotalSegments);
            });

        return TranslateCuesCoreAsync(cues, provider, structuredProgress, cancellationToken);
    }

    public Task<IReadOnlyList<SubtitleCue>> TranslateCuesAsync(
        IReadOnlyList<SubtitleCue> cues,
        ITranslationProvider provider,
        IProgress<TranslationProgress> progress,
        CancellationToken cancellationToken = default) =>
        TranslateCuesCoreAsync(cues, provider, progress, cancellationToken);

    private async Task<IReadOnlyList<SubtitleCue>> TranslateCuesCoreAsync(
        IReadOnlyList<SubtitleCue> cues,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (cues.Count == 0)
            return [];

        var batches = BuildCueBatches(cues, provider.BatchPolicy).ToArray();
        var translated = new List<SubtitleCue>(cues.Count);
        var completed = 0;

        for (var batchOffset = 0; batchOffset < batches.Length; batchOffset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches[batchOffset];
            var batchIndex = batchOffset + 1;
            var startedAt = DateTimeOffset.UtcNow;
            progress?.Report(new TranslationProgress(completed, cues.Count, batchIndex, batches.Length, true, startedAt));

            var segments = batch.Select(c => new TranslationSegment(c.Index, c.Text)).ToArray();
            var result = await provider.TranslateAsync(segments, cancellationToken);

            foreach (var cue in batch)
            {
                if (!result.TryGetValue(cue.Index, out var text))
                    throw new InvalidDataException($"Tłumacz nie zwrócił segmentu {cue.Index}.");
                translated.Add(cue with { Text = text });
                completed++;
            }

            progress?.Report(new TranslationProgress(completed, cues.Count, batchIndex, batches.Length, false, startedAt));
        }

        return translated;
    }

    public Task<IReadOnlyList<string>> TranslateTextLinesAsync(
        IReadOnlyList<string> lines,
        ITranslationProvider provider,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        IProgress<TranslationProgress>? structuredProgress = progress is null
            ? null
            : new InlineProgress<TranslationProgress>(value =>
            {
                if (!value.WaitingForProvider && value.TotalSegments > 0)
                    progress.Report((double)value.CompletedSegments / value.TotalSegments);
            });

        return TranslateTextLinesCoreAsync(lines, provider, structuredProgress, cancellationToken);
    }

    public Task<IReadOnlyList<string>> TranslateTextLinesAsync(
        IReadOnlyList<string> lines,
        ITranslationProvider provider,
        IProgress<TranslationProgress> progress,
        CancellationToken cancellationToken = default) =>
        TranslateTextLinesCoreAsync(lines, provider, progress, cancellationToken);

    private async Task<IReadOnlyList<string>> TranslateTextLinesCoreAsync(
        IReadOnlyList<string> lines,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var nonEmpty = lines.Select((text, index) => new { text, index }).Where(x => !string.IsNullOrWhiteSpace(x.text)).ToArray();
        if (nonEmpty.Length == 0)
            return lines;

        var segments = nonEmpty.Select(x => new TranslationSegment(x.index + 1, x.text)).ToArray();
        var batches = BuildTextBatches(segments, provider.BatchPolicy).ToArray();
        var output = lines.ToArray();
        var completed = 0;

        for (var batchOffset = 0; batchOffset < batches.Length; batchOffset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches[batchOffset];
            var batchIndex = batchOffset + 1;
            var startedAt = DateTimeOffset.UtcNow;
            progress?.Report(new TranslationProgress(completed, nonEmpty.Length, batchIndex, batches.Length, true, startedAt));

            var result = await provider.TranslateAsync(batch, cancellationToken);
            foreach (var segment in batch)
            {
                if (!result.TryGetValue(segment.Id, out var text))
                    throw new InvalidDataException($"Tłumacz nie zwrócił segmentu {segment.Id}.");
                output[segment.Id - 1] = text;
                completed++;
            }

            progress?.Report(new TranslationProgress(completed, nonEmpty.Length, batchIndex, batches.Length, false, startedAt));
        }

        return output;
    }

    private static IEnumerable<IReadOnlyList<SubtitleCue>> BuildCueBatches(
        IReadOnlyList<SubtitleCue> cues,
        TranslationBatchPolicy policy)
    {
        var batch = new List<SubtitleCue>();
        var characters = 0;
        foreach (var cue in cues)
        {
            if (batch.Count > 0 && (batch.Count >= policy.MaxSegments || characters + cue.Text.Length > policy.MaxCharacters))
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

    private static IEnumerable<IReadOnlyList<TranslationSegment>> BuildTextBatches(
        IReadOnlyList<TranslationSegment> segments,
        TranslationBatchPolicy policy)
    {
        var batch = new List<TranslationSegment>();
        var characters = 0;
        foreach (var segment in segments)
        {
            if (batch.Count > 0 && (batch.Count >= policy.MaxSegments || characters + segment.Text.Length > policy.MaxCharacters))
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
