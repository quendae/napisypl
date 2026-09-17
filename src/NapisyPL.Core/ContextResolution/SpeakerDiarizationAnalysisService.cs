using System.Diagnostics;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerDiarizationAnalysisResult(
    IReadOnlyDictionary<int, string?> CueSpeakers,
    int SpeakerSegmentCount,
    IReadOnlyDictionary<string, SpeakerGenderEvidence> SpeakerGenderEvidence,
    IReadOnlyDictionary<int, CueVoiceGenderEvidence> CueGenderEvidence);

public sealed class SpeakerDiarizationAnalysisService(
    AudioContextExtractionService audioExtraction,
    SpeakerDiarizationService diarization,
    IAppLogger? logger = null,
    SpeakerDiarizationCache? cache = null,
    SpeakerVoiceGenderService? voiceGender = null,
    CueVoiceGenderService? cueVoiceGender = null)
{
    public async Task<SpeakerDiarizationAnalysisResult> AnalyzeAsync(
        string mediaPath,
        IReadOnlyList<SubtitleCue> cues,
        IProgress<double>? diarizationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default,
        IReadOnlySet<int>? relevantCueIds = null)
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
                    var uniqueSpeakerCount = CountUniqueSpeakers(cached);
                    logger?.Info(
                        "enhanced_phase",
                        ("file", file),
                        ("stage", "diarization_cache"),
                        ("segmentCount", cached.Count),
                        ("uniqueSpeakerCount", uniqueSpeakerCount),
                        ("result", "hit"));
                    status?.Report($"Enhanced: używam zapisanej analizy rozmówców ({cached.Count} fragmentów, {uniqueSpeakerCount} rozmówców)…");
                    diarizationProgress?.Report(1);

                    var cachedGender = new Dictionary<string, SpeakerGenderEvidence>();
                    IReadOnlyDictionary<int, CueVoiceGenderEvidence> cachedCueGender =
                        new Dictionary<int, CueVoiceGenderEvidence>();
                    if (voiceGender is not null || cueVoiceGender is not null)
                    {
                        var timer = Stopwatch.StartNew();
                        temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
                        logger?.Info("enhanced_phase", ("file", file), ("stage", "audio_extract_gender"), ("elapsedMs", timer.ElapsedMilliseconds), ("result", "success"));

                        if (voiceGender is not null)
                        {
                            cachedGender = new Dictionary<string, SpeakerGenderEvidence>(
                                await voiceGender.AnalyzeAsync(temporaryWave, cached, status, cancellationToken),
                                StringComparer.Ordinal);
                        }

                        if (cueVoiceGender is not null)
                            cachedCueGender = await cueVoiceGender.AnalyzeAsync(
                                temporaryWave, cues, status, cancellationToken, relevantCueIds);
                    }

                    return new SpeakerDiarizationAnalysisResult(
                        SpeakerCueMapper.Map(cues, cached),
                        cached.Count,
                        cachedGender,
                        cachedCueGender);
                }

                logger?.Info("enhanced_phase", ("file", file), ("stage", "diarization_cache"), ("result", "miss"));
            }

            var timerFresh = Stopwatch.StartNew();
            temporaryWave = await audioExtraction.ExtractTemporaryMono16KhzWaveAsync(mediaPath, status, cancellationToken);
            logger?.Info("enhanced_phase", ("file", file), ("stage", "audio_extract"), ("elapsedMs", timerFresh.ElapsedMilliseconds), ("result", "success"));

            status?.Report("Enhanced: rozpoznaję rozmówców lokalnie…");
            timerFresh.Restart();
            var segments = await diarization.AnalyzeAsync(temporaryWave, diarizationProgress, status, cancellationToken);
            var uniqueFreshSpeakerCount = CountUniqueSpeakers(segments);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "diarization"),
                ("elapsedMs", timerFresh.ElapsedMilliseconds),
                ("segmentCount", segments.Count),
                ("uniqueSpeakerCount", uniqueFreshSpeakerCount),
                ("result", "success"));

            if (segments.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się wykryć żadnego mówcy w ścieżce audio.");

            if (cache is not null)
            {
                await cache.SaveAsync(mediaPath, segments, cancellationToken);
                logger?.Info(
                    "enhanced_phase",
                    ("file", file),
                    ("stage", "diarization_cache"),
                    ("segmentCount", segments.Count),
                    ("uniqueSpeakerCount", uniqueFreshSpeakerCount),
                    ("result", "stored"));
            }

            IReadOnlyDictionary<string, SpeakerGenderEvidence> genderEvidence =
                new Dictionary<string, SpeakerGenderEvidence>();
            if (voiceGender is not null)
                genderEvidence = await voiceGender.AnalyzeAsync(temporaryWave, segments, status, cancellationToken);

            IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence =
                new Dictionary<int, CueVoiceGenderEvidence>();
            if (cueVoiceGender is not null)
            {
                cueGenderEvidence = await cueVoiceGender.AnalyzeAsync(
                    temporaryWave, cues, status, cancellationToken, relevantCueIds);
            }

            return new SpeakerDiarizationAnalysisResult(
                SpeakerCueMapper.Map(cues, segments),
                segments.Count,
                genderEvidence,
                cueGenderEvidence);
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

    private static int CountUniqueSpeakers(IReadOnlyList<SpeakerSegment> segments) =>
        segments.Select(segment => segment.Speaker).Distinct().Count();
}
