using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record EnhancedContextAnalysisResult(
    ContextMap Context,
    IReadOnlyDictionary<int, string?> CueSpeakers,
    int SpeakerSegmentCount);

public sealed class EnhancedContextAnalysisService(
    AudioContextExtractionService audioExtraction,
    SpeakerDiarizationService diarization,
    LocalContextRuntimeManager runtimeManager,
    IContextResolver resolver,
    ContextResolutionCoordinator coordinator)
{
    public async Task<EnhancedContextAnalysisResult> AnalyzeAsync(
        string mediaPath,
        IReadOnlyList<SubtitleCue> cues,
        IProgress<double>? diarizationProgress = null,
        IProgress<double>? resolverProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
            throw new ArgumentException("Enhanced analysis requires subtitle cues.", nameof(cues));

        string? temporaryWave = null;
        try
        {
            temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
            var segments = await diarization.AnalyzeAsync(
                temporaryWave,
                diarizationProgress,
                status,
                cancellationToken);

            if (segments.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się wykryć żadnego mówcy w ścieżce audio.");

            var cueSpeakers = SpeakerCueMapper.Map(cues, segments);
            var contextCues = cues.Select(cue => new ContextCue(
                cue.Index,
                cueSpeakers.TryGetValue(cue.Index, out var speaker) ? speaker : null,
                cue.Text,
                null)).ToArray();

            status?.Report("Enhanced: uruchamiam lokalną analizę kontekstu…");
            await runtimeManager.EnsureRunningAsync(status, cancellationToken);

            var context = await coordinator.ResolveAsync(
                contextCues,
                resolver,
                resolverProgress,
                cancellationToken);

            return new EnhancedContextAnalysisResult(context, cueSpeakers, segments.Count);
        }
        finally
        {
            if (temporaryWave is not null)
            {
                try { File.Delete(temporaryWave); } catch { }
            }
        }
    }
}
