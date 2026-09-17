using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Services;

public interface IVideoSubtitleTranslationPipeline : ITranslationPipeline
{
    Task<TranslationResult> TranslateVideoSubtitlesAsync(
        string videoPath,
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}
