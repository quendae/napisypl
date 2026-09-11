using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class SurgicalGenderContextGuardTests
{
    [Fact]
    public void CanApply_SecondPersonEditRequiresKnownAddressee()
    {
        var edit = new SurgicalGenderEdit(
            50,
            "zrobiłeś",
            "zrobiłaś",
            0.99,
            GenderAgreementTarget.Addressee);

        Assert.False(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", probableAddressee: null));
        Assert.True(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", "SPEAKER_B"));
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

        Assert.False(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", "SPEAKER_B"));
    }

    [Fact]
    public void CanApply_FirstPersonEditTargetsSpeaker()
    {
        var edit = new SurgicalGenderEdit(
            7,
            "Byłem",
            "Byłam",
            0.95,
            GenderAgreementTarget.Speaker);

        Assert.True(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", probableAddressee: null));
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

        Assert.False(SurgicalGenderContextGuard.CanApply(edit, "SPEAKER_A", "SPEAKER_B"));
    }
}
