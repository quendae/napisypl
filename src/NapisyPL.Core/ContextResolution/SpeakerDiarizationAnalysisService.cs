using System.Diagnostics;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerDiarizationAnalysisResult(
    IReadOnlyDictionary<int, string?> CueSpeakers,
    int SpeakerSegmentCount,
    IReadOnlyDictionary<string, SpeakerGenderEvidence> SpeakerGenderEvidence);

public sealed class SpeakerDiarizationAnalysisService(
    AudioContextExtractionService audioExtraction,
    SpeakerDiarizationService diarization,
    IAppLogger? logger = null,
    SpeakerDiarizationCache? cache = null,
    SpeakerVoiceGenderService? voiceGender = null)
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
            if (cache is not null)
            {
                var cached = await cache.TryLoadAsync(mediaPath, cancellationToken);
                if (cached is { Count: > 0 })
                {
                    logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization_cache"), ("segmentCount", cached.Count), ("result", "hit"));
                    status?.Report($"Enhanced: używam zapisanej analizy rozmówców ({cached.Count} fragmentów)…");
                    diarizationProgress?.Report(1);

                    var cachedGender = new Dictionary<string, SpeakerGenderEvidence>();
                    if (voiceGender is not null)
                    {
                        var timer = Stopwatch.StartNew();
                        temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
                        logger?.Info("enhanced_phase", ("file", file), ("stage", "audio_extract_gender"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));
                        cachedGender = new Dictionary<string, SpeakerGenderEvidence>(
                            await voiceGender.AnalyzeAsync(temporaryWave, cached, status, cancellationToken),
                            StringComparer.Ordinal);
                    }

                    return new SpeakerDiarizationAnalysisResult(
                        SpeakerCueMapper.Map(cues, cached),
                        cached.Count,
                        cachedGender);
                }

                logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization_cache"), ("result", "miss"));
            }

            var timerFresh = Stopwatch.StartNew();
            temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "audio_extract"), ("elapsedMs", timerFresh.ElapsedMilliseconds), ("result", "success"));

            status?.Report("Enhanced: rozpoznaję rozmówców lokalnie…");
            timerFresh.Restart();
            var segments = await diarization.AnalyzeAsync(temporaryWave, diarizationProgress, status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization"), ("elapsedMs", timerFresh.ElapsedMilliseconds), ("segmentCount", segments.Count), ("result", "success"));

            if (segments.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się wykryć żadnego mówcy w ścieżce audio.");

            if (cache is not null)
            {
                await cache.SaveAsync(mediaPath, segments, cancellationToken);
                logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization_cache"), ("segmentCount", segments.Count), ("result", "stored"));
            }

            IReadOnlyDictionary<string, SpeakerGenderEvidence> genderEvidence =
                new Dictionary<string, SpeakerGenderEvidence>();
            if (voiceGender is not null)
                genderEvidence = await voiceGender.AnalyzeAsync(temporaryWave, segments, status, cancellationToken);

            return new SpeakerDiarizationAnalysisResult(
                SpeakerCueMapper.Map(cues, segments),
                segments.Count,
                genderEvidence);
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
