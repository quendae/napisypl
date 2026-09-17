using System.Collections.Concurrent;
using System.Diagnostics;
using NapisyPL.Core.Diagnostics;

namespace NapisyPL.Core.ContextResolution;

public static class SpeakerGenderSamplePlanner
{
    private const double MinimumSegmentSeconds = 0.75;

    /// <summary>
    /// Pitch tracking costs a fraction of a neural forward pass, so we can afford
    /// a much wider sample than the three the audio-tagging classifier allowed.
    /// More samples directly improve the aggregator's consistency check.
    /// </summary>
    private const int MaximumSegmentsPerSpeaker = 8;

    /// <summary>
    /// Clusters below the aggregator's own minimum total duration can never
    /// produce eligible evidence, so analysing them is pure waste.
    /// </summary>
    private const double MinimumSpeakerSpeechSeconds = 1.5;

    public static IReadOnlyDictionary<int, IReadOnlyList<SpeakerSegment>> SelectSegments(
        IReadOnlyList<SpeakerSegment> segments)
    {
        return segments
            .Where(segment => segment.Speaker >= 0 && segment.EndSeconds - segment.StartSeconds >= MinimumSegmentSeconds)
            .GroupBy(segment => segment.Speaker)
            .Where(group => group.Sum(segment => segment.EndSeconds - segment.StartSeconds) >= MinimumSpeakerSpeechSeconds)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SpeakerSegment>)group
                    .OrderByDescending(segment => segment.EndSeconds - segment.StartSeconds)
                    .Take(MaximumSegmentsPerSpeaker)
                    .ToArray());
    }
}

public sealed class SpeakerVoiceGenderService(IAppLogger? logger = null)
{
    private const double MaximumSampleSeconds = 8;
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;

    public async Task<IReadOnlyDictionary<string, SpeakerGenderEvidence>> AnalyzeAsync(
        string wavePath,
        IReadOnlyList<SpeakerSegment> segments,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return new Dictionary<string, SpeakerGenderEvidence>();

        var timer = Stopwatch.StartNew();
        try
        {
            status?.Report("Enhanced: mierzę wysokość głosu rozmówców…");
            var wave = await Task.Run(() => PcmWaveReader.ReadMono16(wavePath), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var selected = SpeakerGenderSamplePlanner.SelectSegments(segments);
            var evaluations = new ConcurrentDictionary<string, SpeakerGenderEvaluation>(StringComparer.Ordinal);
            var allObservations = new ConcurrentBag<SpeakerGenderObservation>();

            await Task.Run(() =>
            {
                Parallel.ForEach(
                    selected,
                    new ParallelOptions
                    {
                        CancellationToken = cancellationToken,
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
                    },
                    pair =>
                    {
                        var observations = new List<SpeakerGenderObservation>();
                        foreach (var segment in pair.Value)
                        {
                            var samples = SliceSamples(wave, segment, out var durationSeconds);
                            if (samples.Length == 0)
                                continue;

                            var track = FundamentalFrequencyEstimator.Analyze(samples, wave.SampleRate);
                            var observation = VoiceGenderPitchMapper.ToObservation(track, durationSeconds);
                            observations.Add(observation);
                            allObservations.Add(observation);
                        }

                        evaluations[$"SPEAKER_{pair.Key:00}"] =
                            SpeakerGenderEvidenceAggregator.Evaluate(observations);
                    });
            }, cancellationToken);

            var evidence = evaluations.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Evidence,
                StringComparer.Ordinal);

            LogSpeakerDetails(evaluations);

            var known = evidence.Count(pair => pair.Value.Gender != SpeakerVoiceGender.Unknown);
            var diagnostics = SpeakerGenderObservationDiagnostics.Summarize(allObservations.ToArray());
            _logger.Info(
                "enhanced_phase",
                ("stage", "speaker_gender"),
                ("elapsedMs", timer.ElapsedMilliseconds),
                ("speakerCount", evidence.Count),
                ("knownGenderCount", known),
                ("genderSampleCount", diagnostics.SampleCount),
                ("anyGenderTagSampleCount", diagnostics.AnyGenderTagSampleCount),
                ("combinedScoreMeanPermille", diagnostics.CombinedScoreMeanPermille),
                ("combinedScoreMaxPermille", diagnostics.CombinedScoreMaxPermille),
                ("result", "success"));
            return evidence;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Error(
                "enhanced_failed",
                ("stage", "speaker_gender"),
                ("category", ex.GetType().Name),
                ("result", "fallback"));
            status?.Report("Enhanced: pomiar głosów niedostępny — kontynuuję bez tej wskazówki.");
            return new Dictionary<string, SpeakerGenderEvidence>();
        }
    }

    private void LogSpeakerDetails(IReadOnlyDictionary<string, SpeakerGenderEvaluation> evaluations)
    {
        foreach (var pair in evaluations.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var evaluation = pair.Value;
            _logger.Info(
                "speaker_gender_detail",
                ("speaker", pair.Key),
                ("sampleCount", evaluation.Evidence.SampleCount),
                ("voiceGender", evaluation.Evidence.Gender.ToString().ToLowerInvariant()),
                ("reasonCode", evaluation.UnknownReason.ToString()),
                ("combinedMeanPermille", SpeakerGenderObservationDiagnostics.ToPermille(evaluation.CombinedEvidence)),
                ("normalizedWinnerPermille", SpeakerGenderObservationDiagnostics.ToPermille(evaluation.NormalizedWinnerConfidence)),
                ("winnerCount", evaluation.DirectionalWinnerCount),
                ("oppositeCount", evaluation.DirectionalOppositeCount),
                ("requiredWinnerCount", evaluation.DirectionalRequiredCount));
        }
    }

    private static float[] SliceSamples(PcmWaveData wave, SpeakerSegment segment, out double durationSeconds)
    {
        var start = Math.Clamp((int)Math.Floor(segment.StartSeconds * wave.SampleRate), 0, wave.Samples.Length);
        var end = Math.Clamp((int)Math.Ceiling(segment.EndSeconds * wave.SampleRate), start, wave.Samples.Length);
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
