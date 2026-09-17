using System.Collections.Concurrent;
using System.Diagnostics;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed class CueVoiceGenderService(IAppLogger? logger = null)
{
    private const double MaximumSampleSeconds = 8;
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;

    /// <param name="relevantCueIds">
    /// When supplied, only these cues are measured. Everything downstream treats a
    /// missing entry exactly like an unknown one, so narrowing the set changes no
    /// decision - it only avoids tracking pitch for cues whose translation has no
    /// gendered form to correct, and whose neighbours have none either.
    /// </param>
    public async Task<IReadOnlyDictionary<int, CueVoiceGenderEvidence>> AnalyzeAsync(
        string wavePath,
        IReadOnlyList<SubtitleCue> cues,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default,
        IReadOnlySet<int>? relevantCueIds = null)
    {
        if (cues.Count == 0)
            return new Dictionary<int, CueVoiceGenderEvidence>();

        var targets = relevantCueIds is null
            ? cues
            : cues.Where(cue => relevantCueIds.Contains(cue.Index)).ToArray();
        if (targets.Count == 0)
            return new Dictionary<int, CueVoiceGenderEvidence>();

        var timer = Stopwatch.StartNew();
        try
        {
            status?.Report("Enhanced: mierzę wysokość głosu w kolejnych wypowiedziach…");
            var wave = await Task.Run(() => PcmWaveReader.ReadMono16(wavePath), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var evidence = new ConcurrentDictionary<int, CueVoiceGenderEvidence>();
            await Task.Run(() =>
            {
                Parallel.ForEach(
                    targets,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
                    },
                    cue =>
                    {
                        var samples = SliceSamples(wave, cue, out var durationSeconds);
                        if (samples.Length == 0)
                        {
                            evidence[cue.Index] = new CueVoiceGenderEvidence(
                                SpeakerVoiceGender.Unknown, 0, 0, durationSeconds);
                            return;
                        }

                        var track = FundamentalFrequencyEstimator.Analyze(samples, wave.SampleRate);
                        var observation = VoiceGenderPitchMapper.ToObservation(track, durationSeconds);
                        evidence[cue.Index] = CueGenderEvidenceEvaluator.Evaluate(
                            observation.MaleProbability,
                            observation.FemaleProbability,
                            durationSeconds);
                    });
            }, cancellationToken);

            var result = new Dictionary<int, CueVoiceGenderEvidence>(evidence);
            var known = result.Count(pair => pair.Value.Gender != SpeakerVoiceGender.Unknown);
            _logger.Info(
                "enhanced_phase",
                ("stage", "cue_gender"),
                ("elapsedMs", timer.ElapsedMilliseconds),
                ("cueCount", targets.Count),
                ("knownCueGenderCount", known),
                ("result", "success"));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Error(
                "enhanced_failed",
                ("stage", "cue_gender"),
                ("category", ex.GetType().Name),
                ("result", "fallback"));
            status?.Report("Enhanced: pomiar głosu dla kolejnych wypowiedzi niedostępny — używam starszego resolvera rozmówców.");
            return new Dictionary<int, CueVoiceGenderEvidence>();
        }
    }

    private static float[] SliceSamples(PcmWaveData wave, SubtitleCue cue, out double durationSeconds)
    {
        var startSeconds = Math.Max(0, cue.Start.TotalSeconds);
        var endSeconds = Math.Max(startSeconds, cue.End.TotalSeconds);
        var start = Math.Clamp((int)Math.Floor(startSeconds * wave.SampleRate), 0, wave.Samples.Length);
        var end = Math.Clamp((int)Math.Ceiling(endSeconds * wave.SampleRate), start, wave.Samples.Length);
        var available = end - start;
        if (available <= 0)
        {
            durationSeconds = 0;
            return [];
        }

        var maxSamples = (int)Math.Round(MaximumSampleSeconds * wave.SampleRate);
        var length = Math.Min(available, maxSamples);
        var centeredStart = start + Math.Max(0, (available - length) / 2);
        durationSeconds = (double)length / wave.SampleRate;
        return wave.Samples.AsSpan(centeredStart, length).ToArray();
    }
}
