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
    Cancel
}

public sealed record SubtitleFallbackRequest(
    DownloadedSubtitles? AutomaticPolish,
    SubtitleTimingAnalysis? TimingAnalysis,
    bool HasEmbeddedEnglish);

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
