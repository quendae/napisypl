using System.Diagnostics;
using SherpaOnnx;
using NapisyPL.Core.Diagnostics;

namespace NapisyPL.Core.ContextResolution;

public sealed record SpeakerVoiceGenderOptions(
    string BaseDirectory,
    string ModelUrl,
    string LabelsUrl,
    int TopK)
{
    public string ModelPath => Path.Combine(BaseDirectory, "audio-tagging-gender.int8.onnx");
    public string LabelsPath => Path.Combine(BaseDirectory, "audio-tagging-labels.csv");

    public static SpeakerVoiceGenderOptions CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        const string revision = "e4247f89ed94da978e043b3d98a409a32bcbbd34";
        const string root = "https://huggingface.co/k2-fsa/sherpa-onnx-zipformer-small-audio-tagging-2024-04-15/resolve/" + revision + "/";
        return new SpeakerVoiceGenderOptions(
            Path.Combine(localAppData, "SubFlow", "speaker-gender"),
            root + "model.int8.onnx?download=true",
            root + "class_labels_indices.csv?download=true",
            TopK: 50);
    }
}

public sealed class SpeakerVoiceGenderAssetManager(
    HttpClient httpClient,
    SpeakerVoiceGenderOptions options)
{
    public async Task EnsureAvailableAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(options.BaseDirectory);
        if (!File.Exists(options.ModelPath))
        {
            status?.Report("Enhanced: pobieram mały model klasyfikacji głosu (~27 MB)…");
            await DownloadAtomicAsync(options.ModelUrl, options.ModelPath, cancellationToken);
        }

        if (!File.Exists(options.LabelsPath))
            await DownloadAtomicAsync(options.LabelsUrl, options.LabelsPath, cancellationToken);
    }

    private async Task DownloadAtomicAsync(string url, string destination, CancellationToken cancellationToken)
    {
        var partial = destination + ".partial";
        try
        {
            using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 128, useAsync: true))
            {
                await input.CopyToAsync(output, cancellationToken);
                await output.FlushAsync(cancellationToken);
            }
            File.Move(partial, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                try { File.Delete(partial); } catch { }
            }
        }
    }
}

public static class SpeakerGenderSamplePlanner
{
    private const double MinimumSegmentSeconds = 0.75;
    private const int MaximumSegmentsPerSpeaker = 3;

    public static IReadOnlyDictionary<int, IReadOnlyList<SpeakerSegment>> SelectSegments(
        IReadOnlyList<SpeakerSegment> segments)
    {
        return segments
            .Where(segment => segment.Speaker >= 0 && segment.EndSeconds - segment.StartSeconds >= MinimumSegmentSeconds)
            .GroupBy(segment => segment.Speaker)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SpeakerSegment>)group
                    .OrderByDescending(segment => segment.EndSeconds - segment.StartSeconds)
                    .Take(MaximumSegmentsPerSpeaker)
                    .ToArray());
    }
}

public sealed class SpeakerVoiceGenderService(
    SpeakerVoiceGenderAssetManager assetManager,
    SpeakerVoiceGenderOptions options,
    IAppLogger? logger = null)
{
    private const int MaleSpeechLabelIndex = 1;
    private const int FemaleSpeechLabelIndex = 2;
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
            await assetManager.EnsureAvailableAsync(status, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Enhanced: klasyfikuję głosy rozmówców…");

            var wave = await Task.Run(() => PcmWaveReader.ReadMono16(wavePath), cancellationToken);
            var config = new AudioTaggingConfig();
            config.Model.Zipformer.Model = options.ModelPath;
            config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.Model.Provider = "cpu";
            config.Labels = options.LabelsPath;
            config.TopK = options.TopK;

            var selected = SpeakerGenderSamplePlanner.SelectSegments(segments);
            var allObservations = new List<SpeakerGenderObservation>();
            var evaluations = new Dictionary<string, SpeakerGenderEvaluation>(StringComparer.Ordinal);
            var result = await Task.Run<IReadOnlyDictionary<string, SpeakerGenderEvidence>>(() =>
            {
                using var tagger = new AudioTagging(config);
                var evidence = new Dictionary<string, SpeakerGenderEvidence>(StringComparer.Ordinal);

                foreach (var pair in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var observations = new List<SpeakerGenderObservation>();
                    foreach (var segment in pair.Value)
                    {
                        var samples = SliceSamples(wave, segment);
                        if (samples.Length == 0)
                            continue;

                        using var stream = tagger.CreateStream();
                        stream.AcceptWaveform(wave.SampleRate, samples);
                        var events = tagger.Compute(stream);
                        double male = 0;
                        double female = 0;
                        foreach (var audioEvent in events)
                        {
                            if (audioEvent.Index == MaleSpeechLabelIndex)
                                male = audioEvent.Prob;
                            else if (audioEvent.Index == FemaleSpeechLabelIndex)
                                female = audioEvent.Prob;
                        }

                        observations.Add(new SpeakerGenderObservation(
                            male,
                            female,
                            (double)samples.Length / wave.SampleRate));
                    }

                    allObservations.AddRange(observations);
                    var speaker = $"SPEAKER_{pair.Key:00}";
                    var evaluation = SpeakerGenderEvidenceAggregator.Evaluate(observations);
                    evaluations[speaker] = evaluation;
                    evidence[speaker] = evaluation.Evidence;
                }

                return evidence;
            }, CancellationToken.None);

            foreach (var pair in evaluations.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var evaluation = pair.Value;
                _logger.Info(
                    "speaker_gender_detail",
                    ("speaker", pair.Key),
                    ("sampleCount", evaluation.Evidence.SampleCount),
                    ("voiceGender", evaluation.Evidence.Gender.ToString().ToLowerInvariant()),
                    ("reasonCode", evaluation.UnknownReason.ToString()),
                    ("maleMeanPermille", SpeakerGenderObservationDiagnostics.ToPermille(evaluation.MaleEvidence)),
                    ("femaleMeanPermille", SpeakerGenderObservationDiagnostics.ToPermille(evaluation.FemaleEvidence)),
                    ("combinedMeanPermille", SpeakerGenderObservationDiagnostics.ToPermille(evaluation.CombinedEvidence)),
                    ("normalizedWinnerPermille", SpeakerGenderObservationDiagnostics.ToPermille(evaluation.NormalizedWinnerConfidence)),
                    ("winnerCount", evaluation.DirectionalWinnerCount),
                    ("oppositeCount", evaluation.DirectionalOppositeCount),
                    ("requiredWinnerCount", evaluation.DirectionalRequiredCount));
            }

            var known = result.Count(pair => pair.Value.Gender != SpeakerVoiceGender.Unknown);
            var diagnostics = SpeakerGenderObservationDiagnostics.Summarize(allObservations);
            _logger.Info(
                "enhanced_phase",
                ("stage", "speaker_gender"),
                ("elapsedMs", timer.ElapsedMilliseconds),
                ("speakerCount", result.Count),
                ("knownGenderCount", known),
                ("topK", options.TopK),
                ("genderSampleCount", diagnostics.SampleCount),
                ("maleTagSampleCount", diagnostics.MaleTagSampleCount),
                ("femaleTagSampleCount", diagnostics.FemaleTagSampleCount),
                ("anyGenderTagSampleCount", diagnostics.AnyGenderTagSampleCount),
                ("maleScoreMaxPermille", diagnostics.MaleScoreMaxPermille),
                ("femaleScoreMaxPermille", diagnostics.FemaleScoreMaxPermille),
                ("combinedScoreMeanPermille", diagnostics.CombinedScoreMeanPermille),
                ("combinedScoreMaxPermille", diagnostics.CombinedScoreMaxPermille),
                ("result", "success"));
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(
                "enhanced_failed",
                ("stage", "speaker_gender"),
                ("category", ex.GetType().Name),
                ("result", "fallback"));
            status?.Report("Enhanced: klasyfikacja głosów niedostępna — kontynuuję bez tej wskazówki.");
            return new Dictionary<string, SpeakerGenderEvidence>();
        }
    }

    private static float[] SliceSamples(PcmWaveData wave, SpeakerSegment segment)
    {
        var start = Math.Clamp((int)Math.Floor(segment.StartSeconds * wave.SampleRate), 0, wave.Samples.Length);
        var end = Math.Clamp((int)Math.Ceiling(segment.EndSeconds * wave.SampleRate), start, wave.Samples.Length);
        var available = end - start;
        if (available <= 0)
            return [];

        var maxSamples = (int)Math.Round(MaximumSampleSeconds * wave.SampleRate);
        var length = Math.Min(available, maxSamples);
        var centeredStart = start + Math.Max(0, (available - length) / 2);
        return wave.Samples.AsSpan(centeredStart, length).ToArray();
    }
}
