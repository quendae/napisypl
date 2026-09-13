using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class AddresseeGenderReviewSafetyTests
{
    [Fact]
    public void BuildPrompt_ResolvedEligibleAddresseeCreatesExplicitPerCandidateDirective()
    {
        var source = new[]
        {
            Cue(1, "You were ready."),
            Cue(2, "Yes.")
        };
        var translated = new[]
        {
            Cue(1, "Byłeś gotowy."),
            Cue(2, "Tak.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = "SPEAKER_B"
        };
        var probableAddressees = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_A"] = new(SpeakerVoiceGender.Male, 0.97, 3),
            ["SPEAKER_B"] = new(SpeakerVoiceGender.Female, 0.96, 3)
        };

        var prompt = TargetedGenderReviewProtocol.BuildPrompt(
            source,
            translated,
            new HashSet<int> { 1 },
            speakers,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["SPEAKER_A"] = ["You were ready."],
                ["SPEAKER_B"] = ["Yes."]
            },
            probableAddressees,
            evidence);

        Assert.Contains("candidateAddresseeGender", prompt, StringComparison.Ordinal);
        Assert.Contains("\"candidateAddresseeGender\":\"female\"", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"candidateAddresseeGenderConfidence\":0.96", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MUST perform the addressee-target check", prompt, StringComparison.Ordinal);
        Assert.Contains("gender of the probableAddressee", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPrompt_EligibleAddresseeIsRepeatedInMandatoryChecklist()
    {
        var source = new[] { Cue(1, "You forgot about today?") };
        var translated = new[] { Cue(1, "Zapomniałaś o dzisiejszym dniu?") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_A" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_B"] = new(SpeakerVoiceGender.Male, 0.977, 3)
        };

        var prompt = TargetedGenderReviewProtocol.BuildPrompt(
            source,
            translated,
            new HashSet<int> { 1 },
            speakers,
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<int, string?> { [1] = "SPEAKER_B" },
            evidence);

        Assert.Contains("mandatoryAddresseeChecks", prompt, StringComparison.Ordinal);
        Assert.Contains("\"id\":1", prompt, StringComparison.Ordinal);
        Assert.Contains("\"probableAddressee\":\"SPEAKER_B\"", prompt, StringComparison.Ordinal);
        Assert.Contains("\"candidateAddresseeGender\":\"male\"", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"polish\":\"Zapomniałaś o dzisiejszym dniu?\"", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildPrompt_IneligibleAddresseeEvidenceDoesNotAuthorizeAddresseeGender()
    {
        var source = new[] { Cue(1, "You were ready.") };
        var translated = new[] { Cue(1, "Byłeś gotowy.") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_A" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_B"] = new(SpeakerVoiceGender.Female, 0.99, 1)
        };

        var prompt = TargetedGenderReviewProtocol.BuildPrompt(
            source,
            translated,
            new HashSet<int> { 1 },
            speakers,
            new Dictionary<string, IReadOnlyList<string>>(),
            new Dictionary<int, string?> { [1] = "SPEAKER_B" },
            evidence);

        Assert.Contains("\"candidateAddresseeGender\":null", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mandatoryAddresseeChecks:\n[]", prompt.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);
}
