using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

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
    public void BuildPrompt_SingleSampleGenderStaysDiagnosticOnly()
    {
        var source = new[] { Cue(1, "I wanted the business.") };
        var translated = new[] { Cue(1, "Chciałem jego interesy.") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_13" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_13"] = new(SpeakerVoiceGender.Female, 0.94, 1)
        };

        var prompt = TargetedGenderReviewProtocol.BuildPrompt(
            source,
            translated,
            new HashSet<int> { 1 },
            speakers,
            new Dictionary<string, IReadOnlyList<string>> { ["SPEAKER_13"] = ["I wanted the business."] },
            new Dictionary<int, string?> { [1] = null },
            evidence);

        Assert.Contains("\"sampleCount\":1", prompt, StringComparison.Ordinal);
        Assert.Contains("\"candidateSpeakerGender\":null", prompt, StringComparison.Ordinal);
        Assert.Contains("\"candidateSpeakerGenderConfidence\":null", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ContextGuard_RejectsSpeakerEditWithoutEligibleEvidence()
    {
        var edit = new SurgicalGenderEdit(
            141,
            "Chciałem",
            "Chciałam",
            0.96,
            GenderAgreementTarget.Speaker);

        Assert.False(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_13",
            probableAddressee: null,
            currentSpeakerGenderEvidence: null));
        Assert.False(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_13",
            probableAddressee: null,
            currentSpeakerGenderEvidence: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.94, 1)));
    }

    [Fact]
    public void ContextGuard_AllowsSpeakerEditWithEligibleEvidence()
    {
        var edit = new SurgicalGenderEdit(
            141,
            "Chciałem",
            "Chciałam",
            0.96,
            GenderAgreementTarget.Speaker);

        Assert.True(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_13",
            probableAddressee: null,
            currentSpeakerGenderEvidence: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.94, 2)));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);
}
