using System.Diagnostics;
using SherpaOnnx;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed class CueVoiceGenderService(
    SpeakerVoiceGenderAssetManager assetManager,
    SpeakerVoiceGenderOptions options,
    IAppLogger? logger = null)
{
    private const int MaleSpeechLabelIndex = 1;
    private const int FemaleSpeechLabelIndex = 2;
    private const double MaximumSampleSeconds = 8;
    private readonly IAppLogger _logger = logger ?? NullAppLogger.Instance;

    public async Task<IReadOnlyDictionary<int, CueVoiceGenderEvidence>> AnalyzeAsync(
        string wavePath,
        IReadOnlyList<SubtitleCue> cues,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
            return new Dictionary<int, CueVoiceGenderEvidence>();

        var timer = Stopwatch.StartNew();
        try
        {
            await assetManager.EnsureAvailableAsync(status, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            status?.Report("Enhanced: klasyfikuję płeć lokalnie dla kolejnych wypowiedzi…");

            var wave = await Task.Run(() => PcmWaveReader.ReadMono16(wavePath), cancellationToken);
            var config = new AudioTaggingConfig();
            config.Model.Zipformer.Model = options.ModelPath;
            config.Model.NumThreads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
            config.Model.Provider = "cpu";
            config.Labels = options.LabelsPath;
            config.TopK = options.TopK;

            var result = await Task.Run<IReadOnlyDictionary<int, CueVoiceGenderEvidence>>(() =>
            {
                using var tagger = new AudioTagging(config);
                var evidence = new Dictionary<int, CueVoiceGenderEvidence>();

                foreach (var cue in cues)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var samples = SliceSamples(wave, cue, out var durationSeconds);
                    if (samples.Length == 0)
                    {
                        evidence[cue.Index] = new CueVoiceGenderEvidence(
                            SpeakerVoiceGender.Unknown, 0, 0, durationSeconds);
                        continue;
                    }

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

                    evidence[cue.Index] = CueGenderEvidenceEvaluator.Evaluate(male, female, durationSeconds);
                }

                return evidence;
            }, CancellationToken.None);

            var known = result.Count(pair => pair.Value.Gender != SpeakerVoiceGender.Unknown);
            _logger.Info(
                "enhanced_phase",
                ("stage", "cue_gender"),
                ("elapsedMs", timer.ElapsedMilliseconds),
                ("cueCount", cues.Count),
                ("knownCueGenderCount", known),
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
                ("stage", "cue_gender"),
                ("category", ex.GetType().Name),
                ("result", "fallback"));
            status?.Report("Enhanced: lokalna klasyfikacja kolejnych wypowiedzi niedostępna — używam starszego resolvera rozmówców.");
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
