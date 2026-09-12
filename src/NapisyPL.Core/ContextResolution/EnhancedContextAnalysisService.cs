using System.Diagnostics;
using NapisyPL.Core.Diagnostics;
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
    ContextResolutionCoordinator coordinator,
    IAppLogger? logger = null)
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

        var file = Path.GetFileName(mediaPath);
        string? temporaryWave = null;
        try
        {
            var timer = Stopwatch.StartNew();
            temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "audio_extract"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));

            timer.Restart();
            var segments = await diarization.AnalyzeAsync(
                temporaryWave,
                diarizationProgress,
                status,
                cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization"), ("elapsedMs", timer.ElapsedMilliseconds), ("segmentCount", segments.Count), ("result", "success"));

            if (segments.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się wykryć żadnego mówcy w ścieżce audio.");

            var cueSpeakers = SpeakerCueMapper.Map(cues, segments);
            var contextCues = cues.Select(cue => new ContextCue(
                cue.Index,
                cueSpeakers.TryGetValue(cue.Index, out var speaker) ? speaker : null,
                cue.Text,
                null)).ToArray();

            status?.Report("Enhanced: uruchamiam lokalną analizę kontekstu…");
            timer.Restart();
            await runtimeManager.EnsureRunningAsync(status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "resolver_startup"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));

            timer.Restart();
            var context = await coordinator.ResolveAsync(
                contextCues,
                resolver,
                resolverProgress,
                cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "context_resolver"), ("elapsedMs", timer.ElapsedMilliseconds), ("segmentCount", cues.Count), ("result", "success"));

            return new EnhancedContextAnalysisResult(context, cueSpeakers, segments.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.Error("enhanced_failed", ("file", file), ("stage", "context_analysis"), ("category", ex.GetType().Name), ("result", "failed"));
            throw;
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
