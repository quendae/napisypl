using System.Diagnostics;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public sealed class LoggingTranslationProvider(ITranslationProvider inner, IAppLogger logger) : ITranslationProvider
{
    public string DisplayName => inner.DisplayName;
    public TranslationBatchPolicy BatchPolicy => inner.BatchPolicy;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        CancellationToken cancellationToken = default)
    {
        var characterCount = segments.Sum(segment => segment.Text.Length);
        logger.Info(
            "translation_request_start",
            ("provider", inner.DisplayName),
            ("segmentCount", segments.Count),
            ("characterCount", characterCount));

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.TranslateAsync(segments, cancellationToken);
            logger.Info(
                "translation_request_end",
                ("provider", inner.DisplayName),
                ("segmentCount", segments.Count),
                ("characterCount", characterCount),
                ("elapsedMs", Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1)),
                ("result", "success"));
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Error(
                "translation_request_error",
                ("provider", inner.DisplayName),
                ("segmentCount", segments.Count),
                ("characterCount", characterCount),
                ("elapsedMs", Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 1)),
                ("category", ex.GetType().Name),
                ("result", "error"));
            throw;
        }
    }
}
