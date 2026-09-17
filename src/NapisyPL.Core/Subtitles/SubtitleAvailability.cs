using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

public enum PolishSubtitleState
{
    /// <summary>No Polish subtitles were found.</summary>
    Missing,
    /// <summary>A <c>.pl.srt</c> already sat next to the video and was kept.</summary>
    Existing,
    /// <summary>Downloaded, timing checked, and written next to the video.</summary>
    Saved,
    /// <summary>Downloaded but the timing did not match the video safely, so nothing was written.</summary>
    NeedsReview
}

public enum EnglishSubtitleSource
{
    None,
    Embedded,
    Downloaded
}

/// <summary>
/// What the automatic search found for one video: whether Polish subtitles are in
/// place, and whether an English source exists that machine translation could use.
/// </summary>
public sealed record SubtitleAvailability(
    string VideoPath,
    PolishSubtitleState Polish,
    string? PolishProvider,
    string? PolishOutputPath,
    EnglishSubtitleSource English,
    string? EnglishProvider,
    IReadOnlyList<SubtitleCue>? EnglishCues,
    IReadOnlyList<string> Problems)
{
    public bool HasPolish => Polish is PolishSubtitleState.Existing or PolishSubtitleState.Saved;

    public bool CanTranslate => EnglishCues is { Count: > 0 };
}

/// <summary>Result of fitting another release's Polish subtitles onto a video.</summary>
public sealed record PolishSyncOutcome(SubtitleSyncDecision Decision, string? OutputPath, string Detail)
{
    public bool Saved => OutputPath is not null;
}
