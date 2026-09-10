using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public static class ProgressDisplayFormatter
{
    public static string Format(TranslationProgress progress, DateTimeOffset now)
    {
        var text = $"Segment {progress.CompletedSegments} / {progress.TotalSegments} · Partia {progress.BatchIndex} / {progress.BatchCount}";
        if (!progress.WaitingForProvider)
            return text;

        var elapsed = now - progress.BatchStartedAt;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        return $"{text} · {FormatElapsed(elapsed)}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        var totalHours = (int)elapsed.TotalHours;
        return totalHours > 0
            ? $"{totalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}"
            : $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}
