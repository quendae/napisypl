using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class VocativeAddresseeEvidenceTests
{
    [Theory]
    // The line names the person it speaks to.
    [InlineData("But you have, Jaclyn.\nYou survived.", SpeakerVoiceGender.Female)]
    [InlineData("Jaclyn, there's gonna be\nplenty you don't know.", SpeakerVoiceGender.Female)]
    [InlineData("Look, Paula, I know you have\nthe highest standards.", SpeakerVoiceGender.Female)]
    [InlineData("There's nothing wrong\nwith you, Michael.", SpeakerVoiceGender.Male)]
    [InlineData("Mom, somebody's calling you!", SpeakerVoiceGender.Female)]
    [InlineData("There's nothing to say, Dad.", SpeakerVoiceGender.Male)]
    [InlineData("Mr. Wilton, I asked\nyou to do some digging.", SpeakerVoiceGender.Male)]
    [InlineData("Thank you, Mrs. Hudson.", SpeakerVoiceGender.Female)]
    [InlineData("Yes, Sir.", SpeakerVoiceGender.Male)]
    [InlineData("- Hey.\n- Hey, Katie.", SpeakerVoiceGender.Female)]
    public void NamedAddresseeIsRead(string english, SpeakerVoiceGender expected) =>
        Assert.Equal(expected, VocativeAddresseeEvidence.Resolve(english));

    [Theory]
    // Chance S01E01 #294: the name is the therapist being talked about, not the listener.
    [InlineData("the one that you recommended --\nSuzanne.")]
    [InlineData("Suzanne.")]
    // The name is the subject or the object of the sentence.
    [InlineData("Hey. I'm Dr. Heller,\nI'll be working with Dr. Clark.")]
    [InlineData("Ms. Sanders has done nothing\nto violate that.")]
    [InlineData("Jaclyn survived the fire.")]
    [InlineData("when she discovered\nMr. Schorr.")]
    // An apposition introduces a third person instead of addressing her.
    [InlineData("This is my sister, Mary.")]
    [InlineData("Detective Baxter, my partner, was shot.")]
    // A list addresses nobody in particular.
    [InlineData("I called Mary, John and Peter.")]
    // Nothing in the lexicon: a surname, a nickname, an interjection.
    [InlineData("You did it, Blackstone.")]
    [InlineData("Sure, you did.")]
    [InlineData("Sorry, you were saying?")]
    // Short forms that the birth register calls male and television calls female.
    [InlineData("You were right, Sam.")]
    [InlineData("Alex, you promised.")]
    public void EverythingElseStaysUnknown(string english) =>
        Assert.Equal(SpeakerVoiceGender.Unknown, VocativeAddresseeEvidence.Resolve(english));

    [Fact]
    public void TwoNamesOfOppositeGenderCancelOut() =>
        Assert.Equal(SpeakerVoiceGender.Unknown,
            VocativeAddresseeEvidence.Resolve("Jaclyn, tell him.\nMichael, listen to her."));

    [Fact]
    public void WrappedVocativeKeepsItsComma()
    {
        Assert.Equal(SpeakerVoiceGender.Male,
            VocativeAddresseeEvidence.Resolve("State an objection or wait your turn,\nMr. Warwick."));
        Assert.Equal(SpeakerVoiceGender.Female,
            VocativeAddresseeEvidence.Resolve("I only have three followers,\nMom."));
    }
}
