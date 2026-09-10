namespace NapisyPL.Core.Models;

public sealed record TranslationProgress(
    int CompletedSegments,
    int TotalSegments,
    int BatchIndex,
    int BatchCount,
    bool WaitingForProvider,
    DateTimeOffset BatchStartedAt);
