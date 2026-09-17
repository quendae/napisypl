namespace NapisyPL.Core.Models;

public sealed record TranslationResult(
    string PrimaryOutputPath,
    string? TextOutputPath,
    int SegmentCount,
    bool RequiresReview = false,
    string? ReviewMessage = null);
