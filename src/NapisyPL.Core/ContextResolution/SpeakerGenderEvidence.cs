namespace NapisyPL.Core.ContextResolution;

public enum SpeakerVoiceGender
{
    Unknown,
    Male,
    Female
}

public sealed record SpeakerGenderObservation(
    double MaleProbability,
    double FemaleProbability,
    double DurationSeconds);

public sealed record SpeakerGenderEvidence(
    SpeakerVoiceGender Gender,
    double Confidence,
    int SampleCount);

public static class SpeakerGenderEvidenceAggregator
{
    private const double MinimumTotalDurationSeconds = 1.5;
    private const double MinimumCombinedSpeechEvidence = 0.15;
    private const double MinimumNormalizedConfidence = 0.75;
    private const double MinimumPerSampleDirectionConfidence = 0.65;

    public static SpeakerGenderEvidence Aggregate(IReadOnlyList<SpeakerGenderObservation> observations)
    {
        if (observations.Count == 0)
            return new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, 0, 0);

        var valid = observations
            .Where(item =>
                item.DurationSeconds > 0 &&
                double.IsFinite(item.DurationSeconds) &&
                item.MaleProbability is >= 0 and <= 1 &&
                item.FemaleProbability is >= 0 and <= 1)
            .ToArray();
        if (valid.Length == 0)
            return new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, 0, 0);

        var totalDuration = valid.Sum(item => item.DurationSeconds);
        if (totalDuration < MinimumTotalDurationSeconds)
            return new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, 0, valid.Length);

        var male = valid.Sum(item => item.MaleProbability * item.DurationSeconds) / totalDuration;
        var female = valid.Sum(item => item.FemaleProbability * item.DurationSeconds) / totalDuration;
        var combined = male + female;
        if (combined < MinimumCombinedSpeechEvidence)
            return new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, 0, valid.Length);

        var normalizedMale = male / combined;
        var normalizedFemale = female / combined;
        var winner = normalizedMale >= normalizedFemale ? SpeakerVoiceGender.Male : SpeakerVoiceGender.Female;
        var confidence = Math.Max(normalizedMale, normalizedFemale);
        if (confidence < MinimumNormalizedConfidence)
            return new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, confidence, valid.Length);

        var directional = valid.Select(item =>
        {
            var sum = item.MaleProbability + item.FemaleProbability;
            if (sum <= 0)
                return SpeakerVoiceGender.Unknown;
            var normalized = Math.Max(item.MaleProbability, item.FemaleProbability) / sum;
            if (normalized < MinimumPerSampleDirectionConfidence)
                return SpeakerVoiceGender.Unknown;
            return item.MaleProbability >= item.FemaleProbability
                ? SpeakerVoiceGender.Male
                : SpeakerVoiceGender.Female;
        }).ToArray();

        var winnerCount = directional.Count(item => item == winner);
        var oppositeCount = directional.Count(item =>
            item != SpeakerVoiceGender.Unknown && item != winner);
        var requiredWinnerCount = (int)Math.Ceiling(valid.Length * 2d / 3d);
        if (winnerCount < requiredWinnerCount || oppositeCount > valid.Length / 3)
            return new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, confidence, valid.Length);

        return new SpeakerGenderEvidence(winner, confidence, valid.Length);
    }
}
