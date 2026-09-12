using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SurgicalGenderContextGuardTests
{
    [Fact]
    public void CanApply_SecondPersonEditRequiresKnownAddresseeAndEligibleEvidence()
    {
        var edit = new SurgicalGenderEdit(
            50,
            "zrobiłeś",
            "zrobiłaś",
            0.99,
            GenderAgreementTarget.Addressee);

        Assert.False(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", probableAddressee: null));
        Assert.False(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", "SPEAKER_B"));
        Assert.True(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_A",
            "SPEAKER_B",
            probableAddresseeGenderEvidence: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.96, 2)));
    }

    [Fact]
    public void CanApply_RejectsSecondPersonEditClaimedAsSpeakerTarget()
    {
        var edit = new SurgicalGenderEdit(
            50,
            "zrobiłeś",
            "zrobiłaś",
            0.99,
            GenderAgreementTarget.Speaker);

        Assert.False(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_A",
            "SPEAKER_B",
            new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.95, 2)));
    }

    [Fact]
    public void CanApply_FirstPersonEditTargetsSpeakerWhenEvidenceIsEligible()
    {
        var edit = new SurgicalGenderEdit(
            7,
            "Byłem",
            "Byłam",
            0.95,
            GenderAgreementTarget.Speaker);

        Assert.True(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_A",
            probableAddressee: null,
            currentSpeakerGenderEvidence: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.95, 2)));
    }

    [Fact]
    public void CanApply_AddresseeDirectionMustMatchResolvedAddresseeGender()
    {
        var toFemale = new SurgicalGenderEdit(
            50,
            "zrobiłeś",
            "zrobiłaś",
            0.99,
            GenderAgreementTarget.Addressee);
        var toMale = new SurgicalGenderEdit(
            50,
            "zrobiłaś",
            "zrobiłeś",
            0.99,
            GenderAgreementTarget.Addressee);
        var female = new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.97, 3);
        var male = new SpeakerGenderEvidence(SpeakerVoiceGender.Male, 0.97, 3);

        Assert.True(SurgicalGenderContextGuard.CanApply(
            toFemale,
            "SPEAKER_A",
            "SPEAKER_B",
            probableAddresseeGenderEvidence: female));
        Assert.False(SurgicalGenderContextGuard.CanApply(
            toFemale,
            "SPEAKER_A",
            "SPEAKER_B",
            probableAddresseeGenderEvidence: male));
        Assert.True(SurgicalGenderContextGuard.CanApply(
            toMale,
            "SPEAKER_A",
            "SPEAKER_B",
            probableAddresseeGenderEvidence: male));
        Assert.False(SurgicalGenderContextGuard.CanApply(
            toMale,
            "SPEAKER_A",
            "SPEAKER_B",
            probableAddresseeGenderEvidence: female));
    }

    [Theory]
    [InlineData(GenderAgreementTarget.Speaker)]
    [InlineData(GenderAgreementTarget.Addressee)]
    public void CanApply_RejectsBareThirdPersonPastTenseGenderChange(GenderAgreementTarget target)
    {
        var edit = new SurgicalGenderEdit(
            412,
            "wybuchł",
            "wybuchła",
            0.99,
            target);

        Assert.False(SurgicalGenderContextGuard.CanApply(
            edit,
            "SPEAKER_A",
            "SPEAKER_B",
            new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.99, 2),
            new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.99, 2)));
    }
}
