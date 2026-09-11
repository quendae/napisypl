using System.Diagnostics;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerDiarizationAnalysisResult(
    IReadOnlyDictionary<int, string?> CueSpeakers,
    int SpeakerSegmentCount);

public sealed class SpeakerDiarizationAnalysisService(
    AudioContextExtractionService audioExtraction,
    SpeakerDiarizationService diarization,
    IAppLogger? logger = null)
{
    public async Task<SpeakerDiarizationAnalysisResult> AnalyzeAsync(
        string mediaPath,
        IReadOnlyList<SubtitleCue> cues,
        IProgress<double>? diarizationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
            throw new ArgumentException("Speaker analysis requires subtitle cues.", nameof(cues));

        var file = Path.GetFileName(mediaPath);
        string? temporaryWave = null;
        try
        {
            var timer = Stopwatch.StartNew();
            temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "audio_extract"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));

            status?.Report("Enhanced: rozpoznaję rozmówców lokalnie…");
            timer.Restart();
            var segments = await diarization.AnalyzeAsync(temporaryWave, diarizationProgress, status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization"), ("elapsedMs", timer.ElapsedMilliseconds), ("segmentCount", segments.Count), ("result", "success"));

            if (segments.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się wykryć żadnego mówcy w ścieżce audio.");

            return new SpeakerDiarizationAnalysisResult(SpeakerCueMapper.Map(cues, segments), segments.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.Error("enhanced_failed", ("file", file), ("stage", "speaker_analysis"), ("category", ex.GetType().Name), ("result", "failed"));
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
