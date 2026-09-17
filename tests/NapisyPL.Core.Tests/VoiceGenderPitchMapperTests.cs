using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public class VoiceGenderPitchMapperTests
{
    private static PitchTrack Track(double medianF0Hz, double voicedFraction, int totalFrames = 50)
    {
        var voicedCount = (int)Math.Round(voicedFraction * totalFrames);
        var values = Enumerable.Repeat(medianF0Hz, voicedCount).ToArray();
        return new PitchTrack(values, totalFrames);
    }

    [Theory]
    [InlineData(95)]
    [InlineData(110)]
    [InlineData(125)]
    public void MaleRangePitch_ProducesMaleDirectionAboveEligibilityConfidence(double f0Hz)
    {
        var observation = VoiceGenderPitchMapper.ToObservation(Track(f0Hz, 0.8), 3.0);

        var combined = observation.MaleProbability + observation.FemaleProbability;
        Assert.True(observation.MaleProbability > observation.FemaleProbability);
        Assert.True(
            observation.MaleProbability / combined >= SpeakerGenderReviewEligibility.MinimumConfidence,
            $"normalized={observation.MaleProbability / combined}");
    }

    [Theory]
    [InlineData(200)]
    [InlineData(215)]
    [InlineData(240)]
    public void FemaleRangePitch_ProducesFemaleDirectionAboveEligibilityConfidence(double f0Hz)
    {
        var observation = VoiceGenderPitchMapper.ToObservation(Track(f0Hz, 0.8), 3.0);

        var combined = observation.MaleProbability + observation.FemaleProbability;
        Assert.True(observation.FemaleProbability > observation.MaleProbability);
        Assert.True(
            observation.FemaleProbability / combined >= SpeakerGenderReviewEligibility.MinimumConfidence,
            $"normalized={observation.FemaleProbability / combined}");
    }

    [Fact]
    public void AmbiguousPitchNearBoundary_StaysBelowEveryDownstreamGate()
    {
        var observation = VoiceGenderPitchMapper.ToObservation(Track(168, 0.8), 3.0);

        var combined = observation.MaleProbability + observation.FemaleProbability;
        var normalized = Math.Max(observation.MaleProbability, observation.FemaleProbability) / combined;
        Assert.True(normalized < 0.82, $"normalized={normalized}");
    }

    [Fact]
    public void LowVoicedFraction_YieldsNoEvidence()
    {
        var observation = VoiceGenderPitchMapper.ToObservation(Track(120, 0.05), 3.0);

        Assert.Equal(0, observation.MaleProbability);
        Assert.Equal(0, observation.FemaleProbability);
    }

    [Fact]
    public void CombinedEvidenceTracksVoicedFraction()
    {
        var observation = VoiceGenderPitchMapper.ToObservation(Track(120, 0.75), 3.0);

        Assert.InRange(observation.MaleProbability + observation.FemaleProbability, 0.74, 0.76);
    }

    [Fact]
    public void ClearMaleSpeech_SurvivesTheFullAggregatorAndEligibilityChain()
    {
        var observations = new[]
        {
            VoiceGenderPitchMapper.ToObservation(Track(118, 0.7), 3.0),
            VoiceGenderPitchMapper.ToObservation(Track(124, 0.8), 2.5),
            VoiceGenderPitchMapper.ToObservation(Track(112, 0.6), 2.0)
        };

        var evidence = SpeakerGenderEvidenceAggregator.Aggregate(observations);

        Assert.Equal(SpeakerVoiceGender.Male, evidence.Gender);
        Assert.True(SpeakerGenderReviewEligibility.IsEligible(evidence));
    }

    [Fact]
    public void ClearFemaleSpeech_SurvivesTheFullAggregatorAndEligibilityChain()
    {
        var observations = new[]
        {
            VoiceGenderPitchMapper.ToObservation(Track(205, 0.7), 3.0),
            VoiceGenderPitchMapper.ToObservation(Track(220, 0.8), 2.5),
            VoiceGenderPitchMapper.ToObservation(Track(198, 0.6), 2.0)
        };

        var evidence = SpeakerGenderEvidenceAggregator.Aggregate(observations);

        Assert.Equal(SpeakerVoiceGender.Female, evidence.Gender);
        Assert.True(SpeakerGenderReviewEligibility.IsEligible(evidence));
    }

    [Fact]
    public void MixedSpeakersInOneCluster_RemainUnknown()
    {
        var observations = new[]
        {
            VoiceGenderPitchMapper.ToObservation(Track(115, 0.7), 3.0),
            VoiceGenderPitchMapper.ToObservation(Track(215, 0.8), 2.5)
        };

        var evidence = SpeakerGenderEvidenceAggregator.Aggregate(observations);

        Assert.Equal(SpeakerVoiceGender.Unknown, evidence.Gender);
    }

    [Fact]
    public void CueEvaluator_AcceptsClearPitchAndRejectsAmbiguousPitch()
    {
        var clear = VoiceGenderPitchMapper.ToObservation(Track(210, 0.8), 3.0);
        var ambiguous = VoiceGenderPitchMapper.ToObservation(Track(168, 0.8), 3.0);

        var clearEvidence = CueGenderEvidenceEvaluator.Evaluate(
            clear.MaleProbability, clear.FemaleProbability, 3.0);
        var ambiguousEvidence = CueGenderEvidenceEvaluator.Evaluate(
            ambiguous.MaleProbability, ambiguous.FemaleProbability, 3.0);

        Assert.Equal(SpeakerVoiceGender.Female, clearEvidence.Gender);
        Assert.Equal(SpeakerVoiceGender.Unknown, ambiguousEvidence.Gender);
    }

    [Fact]
    public void UnvoicedSamples_AbstainInsteadOfBreakingConsistency()
    {
        // Seen on S01E08: 8 samples, one clearly male, the rest music or silence,
        // reported as "InconsistentSamples".
        var observations = new List<SpeakerGenderObservation>
        {
            VoiceGenderPitchMapper.ToObservation(Track(115, 0.7), 4.0),
            VoiceGenderPitchMapper.ToObservation(Track(120, 0.6), 3.0)
        };
        for (var index = 0; index < 6; index++)
            observations.Add(VoiceGenderPitchMapper.ToObservation(Track(0, 0), 3.0));

        var evidence = SpeakerGenderEvidenceAggregator.Evaluate(observations);

        Assert.Equal(SpeakerVoiceGender.Male, evidence.Evidence.Gender);
        Assert.Equal(2, evidence.Evidence.SampleCount);
    }
}
