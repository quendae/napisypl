using System.Diagnostics;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Services;

public sealed class EnhancedTranslationPipeline(
    ITranslationPipeline standardPipeline,
    SubtitleExtractionService extractionService,
    SrtParser srtParser,
    SubtitleWriter writer,
    TranslationCoordinator translationCoordinator,
    SpeakerDiarizationAnalysisService speakerAnalysis,
    DeterministicGenderReviewService deterministicReview,
    IAppLogger? logger = null) : ITranslationPipeline
{
    public bool HardVoiceTurnOnly { get; set; }

    public async Task<TranslationResult> TranslateAsync(
        string inputPath,
        SubtitleTrack? selectedTrack,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<TranslationProgress>? translationProgress = null,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        if (!TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(inputPath)))
        {
            status?.Report("Enhanced wymaga ścieżki audio; dla tego pliku używam trybu Standard.");
            return await standardPipeline.TranslateAsync(
                inputPath, selectedTrack, provider, exportTxt,
                translationProgress, status, cancellationToken);
        }

        if (selectedTrack is null || !selectedTrack.IsText)
            throw new InvalidOperationException("Enhanced wymaga tekstowej ścieżki napisów.");

        var file = Path.GetFileName(inputPath);
        logger?.Info(
            "translation_options",
            ("file", file),
            ("provider", provider.DisplayName),
            ("exportTxt", exportTxt),
            ("enhanced", true),
            ("hardVoiceMode", HardVoiceTurnOnly ? "forced_binary" : "off"));

        string? temporarySrt = null;
        try
        {
            status?.Report("Enhanced: wyciągam napisy z filmu…");
            temporarySrt = await extractionService.ExtractToTemporarySrtAsync(
                inputPath, selectedTrack, status, cancellationToken);

            var content = await File.ReadAllTextAsync(temporarySrt, Encoding.UTF8, cancellationToken);
            var sourceCues = srtParser.Parse(content);
            if (sourceCues.Count == 0)
                throw new InvalidDataException("Enhanced: nie udało się odczytać żadnych kwestii z napisów.");

            status?.Report("Enhanced: analizuję rozmówców i lokalne zmiany głosu w audio…");
            var audioTimer = Stopwatch.StartNew();
            var speakers = await speakerAnalysis.AnalyzeAsync(
                inputPath,
                sourceCues,
                diarizationProgress: null,
                status,
                cancellationToken);
            var knownGenderCount = speakers.SpeakerGenderEvidence.Count(pair =>
                SpeakerGenderReviewEligibility.IsEligible(pair.Value));
            var knownCueGenderCount = speakers.CueGenderEvidence.Count(pair =>
                pair.Value.Gender != SpeakerVoiceGender.Unknown);
            var directionalCueGenderCount = speakers.CueGenderEvidence.Count(pair =>
                HardVoiceTurnResolver.TryGetForcedGender(
                    speakers.CueGenderEvidence,
                    pair.Key,
                    out _,
                    out _));
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "speaker_analysis"),
                ("elapsedMs", audioTimer.ElapsedMilliseconds),
                ("segmentCount", speakers.SpeakerSegmentCount),
                ("knownGenderCount", knownGenderCount),
                ("knownCueGenderCount", knownCueGenderCount),
                ("directionalCueGenderCount", directionalCueGenderCount),
                ("result", "success"));

            status?.Report(HardVoiceTurnOnly
                ? $"Enhanced test M/K: {directionalCueGenderCount}/{sourceCues.Count} wypowiedzi ma kierunek głosu M albo K. Tłumaczę przez {provider.DisplayName}…"
                : $"Enhanced: {knownCueGenderCount}/{sourceCues.Count} wypowiedzi ma pewną lokalną klasyfikację głosu. Tłumaczę przez {provider.DisplayName}…");
            var translationTimer = Stopwatch.StartNew();
            var translated = await TranslateNormallyAsync(
                sourceCues,
                provider,
                translationProgress,
                cancellationToken);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "translation"),
                ("provider", provider.DisplayName),
                ("elapsedMs", translationTimer.ElapsedMilliseconds),
                ("segmentCount", translated.Count),
                ("result", "success"));

            cancellationToken.ThrowIfCancellationRequested();
            status?.Report(HardVoiceTurnOnly
                ? "Enhanced test M/K: wymuszam płeć z kierunku audio; przy zmianie M↔K poprawiam formy adresata…"
                : "Enhanced: poprawiam tylko bezpieczne formy rodzaju na podstawie kolejności wypowiedzi…");
            var reviewTimer = Stopwatch.StartNew();
            var genderDiagnostics = new List<DeterministicGenderCueDiagnostic>();
            var reviewed = deterministicReview.Review(
                sourceCues,
                translated,
                speakers.CueSpeakers,
                speakers.SpeakerGenderEvidence,
                speakers.CueGenderEvidence,
                genderDiagnostics,
                hardVoiceTurnOnly: HardVoiceTurnOnly);
            var changedCount = reviewed.Zip(translated)
                .Count(pair => !string.Equals(pair.First.Text, pair.Second.Text, StringComparison.Ordinal));

            var sourcePositions = sourceCues
                .Select((cue, position) => (cue.Index, position))
                .ToDictionary(pair => pair.Index, pair => pair.position);
            foreach (var diagnostic in genderDiagnostics)
            {
                SubtitleCue? nextCue = null;
                if (sourcePositions.TryGetValue(diagnostic.CueId, out var position) && position + 1 < sourceCues.Count)
                    nextCue = sourceCues[position + 1];

                SpeakerGenderEvidence? currentSpeakerGender = null;
                if (!string.IsNullOrWhiteSpace(diagnostic.CurrentSpeaker) &&
                    speakers.SpeakerGenderEvidence.TryGetValue(diagnostic.CurrentSpeaker!, out var currentGender))
                {
                    currentSpeakerGender = currentGender;
                }

                speakers.CueGenderEvidence.TryGetValue(diagnostic.CueId, out var currentCueGender);
                var currentForcedGender = SpeakerVoiceGender.Unknown;
                var currentForcedConfidence = 0d;
                HardVoiceTurnResolver.TryGetForcedGender(
                    speakers.CueGenderEvidence,
                    diagnostic.CueId,
                    out currentForcedGender,
                    out currentForcedConfidence);

                string? nextSpeaker = null;
                CueVoiceGenderEvidence? nextCueGender = null;
                SpeakerGenderEvidence? nextSpeakerGender = null;
                var nextForcedGender = SpeakerVoiceGender.Unknown;
                var nextForcedConfidence = 0d;
                if (nextCue is not null)
                {
                    speakers.CueSpeakers.TryGetValue(nextCue.Index, out nextSpeaker);
                    if (speakers.CueGenderEvidence.TryGetValue(nextCue.Index, out var cueGender))
                        nextCueGender = cueGender;
                    HardVoiceTurnResolver.TryGetForcedGender(
                        speakers.CueGenderEvidence,
                        nextCue.Index,
                        out nextForcedGender,
                        out nextForcedConfidence);
                    if (!string.IsNullOrWhiteSpace(nextSpeaker) &&
                        speakers.SpeakerGenderEvidence.TryGetValue(nextSpeaker!, out var speakerGender))
                    {
                        nextSpeakerGender = speakerGender;
                    }
                }

                logger?.Info(
                    "enhanced_gender_cue",
                    ("cue", diagnostic.CueId),
                    ("speaker", diagnostic.CurrentSpeaker ?? "none"),
                    ("speakerGender", currentSpeakerGender?.Gender.ToString() ?? "none"),
                    ("speakerConfidencePermille", currentSpeakerGender is null ? 0 : ToPermille(currentSpeakerGender.Confidence)),
                    ("speakerSampleCount", currentSpeakerGender?.SampleCount ?? 0),
                    ("cueGender", currentCueGender?.Gender.ToString() ?? "none"),
                    ("cueDirectionalGender", currentCueGender?.DirectionalGender.ToString() ?? "none"),
                    ("cueDirectionalConfidencePermille", currentCueGender is null ? 0 : ToPermille(currentCueGender.DirectionalConfidence)),
                    ("forcedCueGender", currentForcedGender),
                    ("forcedCueConfidencePermille", ToPermille(currentForcedConfidence)),
                    ("candidate", diagnostic.CandidateWord ?? "none"),
                    ("resolver", diagnostic.Resolver),
                    ("reasonCode", diagnostic.ReasonCode),
                    ("targetGender", diagnostic.TargetGender),
                    ("confidencePermille", ToPermille(diagnostic.Confidence)),
                    ("gate", diagnostic.GatePassed ? "pass" : "fail"),
                    ("nextCue", nextCue?.Index ?? -1),
                    ("nextSpeaker", nextSpeaker ?? "none"),
                    ("nextCueGender", nextCueGender?.Gender.ToString() ?? "none"),
                    ("nextCueDirectionalGender", nextCueGender?.DirectionalGender.ToString() ?? "none"),
                    ("nextCueDirectionalConfidencePermille", nextCueGender is null ? 0 : ToPermille(nextCueGender.DirectionalConfidence)),
                    ("nextForcedCueGender", nextForcedGender),
                    ("nextForcedCueConfidencePermille", ToPermille(nextForcedConfidence)),
                    ("nextCueCombinedPermille", nextCueGender is null ? 0 : ToPermille(nextCueGender.CombinedEvidence)),
                    ("nextCueDurationMs", nextCueGender is null ? 0 : (int)Math.Round(nextCueGender.DurationSeconds * 1000)),
                    ("nextSpeakerGender", nextSpeakerGender?.Gender.ToString() ?? "none"),
                    ("nextSpeakerConfidencePermille", nextSpeakerGender is null ? 0 : ToPermille(nextSpeakerGender.Confidence)),
                    ("nextSpeakerSampleCount", nextSpeakerGender?.SampleCount ?? 0),
                    ("matchedWord", diagnostic.MatchedWord ?? "none"),
                    ("replacement", diagnostic.Replacement ?? "none"),
                    ("changed", diagnostic.Changed));
            }

            var hardVoiceResolvedCount = genderDiagnostics.Count(item =>
                string.Equals(item.Resolver, "hard_voice_sequence", StringComparison.Ordinal) && item.GatePassed);
            var hardVoiceChangedCount = genderDiagnostics.Count(item =>
                string.Equals(item.Resolver, "hard_voice_sequence", StringComparison.Ordinal) && item.Changed);
            var localTurnResolvedCount = HardVoiceTurnOnly
                ? 0
                : sourceCues.Count(cue =>
                    LocalTurnGenderResolver.Resolve(
                        sourceCues,
                        speakers.CueSpeakers,
                        speakers.CueGenderEvidence,
                        cue.Index,
                        speakers.SpeakerGenderEvidence).IsResolved);
            var resolvedAddresseeCount = HardVoiceTurnOnly
                ? 0
                : sourceCues.Count(cue =>
                    DialogueAddresseeResolver.ResolveDetailed(sourceCues, speakers.CueSpeakers, cue.Index).IsResolved);
            logger?.Info(
                "enhanced_phase",
                ("file", file),
                ("stage", "deterministic_gender_review"),
                ("reviewMode", HardVoiceTurnOnly ? "hard_voice_forced_binary" : "standard"),
                ("elapsedMs", reviewTimer.ElapsedMilliseconds),
                ("segmentCount", translated.Count),
                ("knownGenderCount", knownGenderCount),
                ("knownCueGenderCount", knownCueGenderCount),
                ("directionalCueGenderCount", directionalCueGenderCount),
                ("hardVoiceResolvedCount", hardVoiceResolvedCount),
                ("hardVoiceChangedCount", hardVoiceChangedCount),
                ("localTurnResolvedCount", localTurnResolvedCount),
                ("resolvedAddresseeCount", resolvedAddresseeCount),
                ("candidateCount", genderDiagnostics.Count),
                ("completed", changedCount),
                ("result", "success"));

            var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
            var stem = Path.GetFileNameWithoutExtension(inputPath);
            var srtOutput = Path.Combine(directory, stem + ".pl.srt");
            status?.Report(HardVoiceTurnOnly
                ? $"Enhanced test M/K: zapisuję wynik — zmieniono {changedCount} kwestii…"
                : $"Enhanced: zapisuję wynik — deterministyczny korektor zmienił {changedCount} kwestii…");
            await writer.WriteSrtAsync(srtOutput, reviewed, cancellationToken);

            string? txtOutput = null;
            if (exportTxt)
            {
                txtOutput = Path.Combine(directory, stem + ".pl.txt");
                await writer.WriteTxtAsync(txtOutput, reviewed, cancellationToken);
            }

            return new TranslationResult(srtOutput, txtOutput, reviewed.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.Error(
                "enhanced_failed",
                ("file", file),
                ("stage", "translation_pipeline"),
                ("category", ex.GetType().Name),
                ("result", "failed"));
            throw;
        }
        finally
        {
            if (temporarySrt is not null)
            {
                try { File.Delete(temporarySrt); } catch { }
            }
        }
    }

    private static int ToPermille(double value) =>
        (int)Math.Round(Math.Clamp(value, 0, 1) * 1000, MidpointRounding.AwayFromZero);

    private Task<IReadOnlyList<SubtitleCue>> TranslateNormallyAsync(
        IReadOnlyList<SubtitleCue> sourceCues,
        ITranslationProvider provider,
        IProgress<TranslationProgress>? translationProgress,
        CancellationToken cancellationToken) =>
        translationProgress is null
            ? translationCoordinator.TranslateCuesAsync(
                sourceCues,
                provider,
                progress: (IProgress<double>?)null,
                cancellationToken)
            : translationCoordinator.TranslateCuesAsync(
                sourceCues,
                provider,
                translationProgress,
                cancellationToken);
}
