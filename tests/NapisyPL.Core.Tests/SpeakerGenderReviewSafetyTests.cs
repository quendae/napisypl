using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SpeakerGenderReviewSafetyTests
{
    [Fact]
    public void Eligibility_RequiresAtLeastTwoSamples()
    {
        Assert.False(SpeakerGenderReviewEligibility.IsEligible(
            new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.99, 1)));
        Assert.True(SpeakerGenderReviewEligibility.IsEligible(
            new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.94, 2)));
    }

    [Fact]
    public void Eligibility_RejectsUnknownOrLowConfidenceEvidence()
    {
        Assert.False(SpeakerGenderReviewEligibility.IsEligible(
            new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, 0.99, 3)));
        Assert.False(SpeakerGenderReviewEligibility.IsEligible(
            new SpeakerGenderEvidence(SpeakerVoiceGender.Male, 0.80, 3)));
    }
}
