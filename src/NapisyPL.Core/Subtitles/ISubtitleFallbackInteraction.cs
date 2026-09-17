using System.ComponentModel;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Subtitles;

public enum SubtitleFallbackAction
{
    DownloadInteractivePolish,
    UseLocalPolishSrt,
    ApplyRecommendedTransform,
    UseWithoutChanges,
    TranslateEmbeddedEnglish,
    ContinueWithEnglishFallback,
    Cancel
}

public sealed record SubtitleFallbackRequest(
    DownloadedSubtitles? PolishCandidate,
    SubtitleTimingAnalysis? TimingAnalysis,
    bool HasEmbeddedTextTrack,
    bool HasAutomaticallyTranslatableEmbeddedEnglish,
    bool PolishSearchReportedNoSubtitles = false);

/// <summary>
/// A selection from the single-file fallback UI. Interactive QNapi supplies its parsed Polish candidate here so
/// the acquisition pipeline can validate and synchronize it using the same policy as automatic and local sources.
/// </summary>
public sealed record SubtitleFallbackChoice(
    SubtitleFallbackAction Action,
    string? LocalSrtPath = null,
    DownloadedSubtitles? InteractivePolish = null);

public interface ISubtitleFallbackInteraction
{
    Task<SubtitleFallbackChoice> ChooseAsync(
        SubtitleFallbackRequest request,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}
