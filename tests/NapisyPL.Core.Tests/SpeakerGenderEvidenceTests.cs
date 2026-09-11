using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerGenderEvidenceTests
{
    [Fact]
    public void Aggregate_StrongConsistentMaleObservations_ReturnsMale()
    {
        var result = SpeakerGenderEvidenceAggregator.Aggregate(
        [
            new SpeakerGenderObservation(0.82, 0.08, 2.5),
            new SpeakerGenderObservation(0.74, 0.12, 3.0),
            new SpeakerGenderObservation(0.79, 0.10, 1.8)
        ]);

        Assert.Equal(SpeakerVoiceGender.Male, result.Gender);
        Assert.True(result.Confidence >= 0.8);
        Assert.Equal(3, result.SampleCount);
    }

    [Fact]
    public void Aggregate_ConflictingObservations_ReturnsUnknown()
    {
        var result = SpeakerGenderEvidenceAggregator.Aggregate(
        [
            new SpeakerGenderObservation(0.70, 0.10, 2.0),
            new SpeakerGenderObservation(0.12, 0.72, 2.0)
        ]);

        Assert.Equal(SpeakerVoiceGender.Unknown, result.Gender);
        Assert.Equal(2, result.SampleCount);
    }

    [Fact]
    public void Evaluate_LowAbsoluteButDirectionalEvidence_ReportsLowCombinedReason()
    {
        var result = SpeakerGenderEvidenceAggregator.Evaluate(
        [
            new SpeakerGenderObservation(0.040, 0.004, 3.0),
            new SpeakerGenderObservation(0.031, 0.003, 2.0),
            new SpeakerGenderObservation(0.025, 0.004, 1.5)
        ]);

        Assert.Equal(SpeakerVoiceGender.Unknown, result.Evidence.Gender);
        Assert.Equal(SpeakerGenderUnknownReason.LowCombinedEvidence, result.UnknownReason);
        Assert.InRange(result.CombinedEvidence, 0.03, 0.05);
        Assert.True(result.NormalizedWinnerConfidence > 0.85);
    }

    [Fact]
    public void Evaluate_StrongConsistentFemaleEvidence_ReportsKnownWithoutUnknownReason()
    {
        var result = SpeakerGenderEvidenceAggregator.Evaluate(
        [
            new SpeakerGenderObservation(0.05, 0.55, 2.0),
            new SpeakerGenderObservation(0.06, 0.61, 2.5)
        ]);

        Assert.Equal(SpeakerVoiceGender.Female, result.Evidence.Gender);
        Assert.Equal(SpeakerGenderUnknownReason.None, result.UnknownReason);
        Assert.True(result.NormalizedWinnerConfidence > 0.85);
    }

    [Fact]
    public void Diagnostics_SummarizeTagPresenceAndScoresWithoutAudioContent()
    {
        var result = SpeakerGenderObservationDiagnostics.Summarize(
        [
            new SpeakerGenderObservation(0.42, 0.03, 2.0),
            new SpeakerGenderObservation(0.00, 0.31, 3.0),
            new SpeakerGenderObservation(0.00, 0.00, 1.0)
        ]);

        Assert.Equal(3, result.SampleCount);
        Assert.Equal(1, result.MaleTagSampleCount);
        Assert.Equal(2, result.FemaleTagSampleCount);
        Assert.Equal(2, result.AnyGenderTagSampleCount);
        Assert.Equal(420, result.MaleScoreMaxPermille);
        Assert.Equal(310, result.FemaleScoreMaxPermille);
        Assert.Equal(305, result.CombinedScoreMeanPermille);
        Assert.Equal(450, result.CombinedScoreMaxPermille);
    }

    [Fact]
    public void BuildPrompt_IncludesRelevantSpeakerVoiceGenderEvidence()
    {
        var source = new[] { Cue(1, "I was ready."), Cue(2, "Okay.") };
        var translated = new[] { Cue(1, "Byłem gotowy."), Cue(2, "Dobrze.") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_00", [2] = "SPEAKER_01" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_00"] = new(SpeakerVoiceGender.Female, 0.93, 3),
            ["SPEAKER_01"] = new(SpeakerVoiceGender.Male, 0.91, 2)
        };

        var prompt = TargetedGenderReviewProtocol.BuildPrompt(
            source,
            translated,
            new HashSet<int> { 1 },
            speakers,
            new Dictionary<string, IReadOnlyList<string>> { ["SPEAKER_00"] = ["I was ready."] },
            new Dictionary<int, string?> { [1] = null },
            evidence);

        Assert.Contains("speakerGenderEvidence", prompt, StringComparison.Ordinal);
        Assert.Contains("SPEAKER_00", prompt, StringComparison.Ordinal);
        Assert.Contains("female", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SPEAKER_01\":{\"gender\"", prompt, StringComparison.Ordinal);
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);
}
