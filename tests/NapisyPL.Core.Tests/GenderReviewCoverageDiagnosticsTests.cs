using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class GenderReviewCoverageDiagnosticsTests
{
    [Fact]
    public void Summarize_SeparatesKnownSpeakerAndKnownAddresseeCoverage()
    {
        var cues = new[]
        {
            Cue(1, 0, 1, "B before"),
            Cue(2, 1.2, 2.2, "candidate A"),
            Cue(3, 2.4, 3.4, "B after"),
            Cue(4, 20.0, 21.0, "candidate unknown")
        };
        var candidateIds = new HashSet<int> { 2, 4 };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_B",
            [2] = "SPEAKER_A",
            [3] = "SPEAKER_B",
            [4] = "SPEAKER_C"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_A"] = new(SpeakerVoiceGender.Female, 0.94, 3),
            ["SPEAKER_B"] = new(SpeakerVoiceGender.Male, 0.98, 3),
            ["SPEAKER_C"] = new(SpeakerVoiceGender.Unknown, 0.91, 2)
        };

        var result = GenderReviewCoverageDiagnostics.Summarize(cues, candidateIds, speakers, evidence);

        Assert.Equal(2, result.CandidateCount);
        Assert.Equal(1, result.KnownSpeakerCandidateCount);
        Assert.Equal(new[] { 2 }, result.KnownSpeakerCandidateIds);
        Assert.Equal(1, result.KnownAddresseeCandidateCount);
        Assert.Equal(new[] { 2 }, result.KnownAddresseeCandidateIds);
        Assert.Equal(1, result.EligibleAddresseeCandidateCount);
        Assert.Equal(new[] { 2 }, result.EligibleAddresseeCandidateIds);
        Assert.Equal(2, result.KnownRelevantGenderEvidenceCount);
        Assert.Equal(1, result.EligibleSpeakerCandidateCount);
        Assert.Equal(new[] { 2 }, result.EligibleSpeakerCandidateIds);
        Assert.Equal(
            "2:SPEAKER_A:female:940:3:true;4:SPEAKER_C:unknown:910:2:false|addressee=2:SPEAKER_B:990:sandwich_turn:male:980:3:true",
            result.SpeakerCandidateEvidence);
    }

    [Fact]
    public void Summarize_KnownSingleSampleSpeakerIsReportedButNotEligible()
    {
        var cues = new[] { Cue(141, 10, 11, "candidate") };
        var speakers = new Dictionary<int, string?> { [141] = "SPEAKER_13" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_13"] = new(SpeakerVoiceGender.Female, 0.94, 1)
        };

        var result = GenderReviewCoverageDiagnostics.Summarize(
            cues,
            new HashSet<int> { 141 },
            speakers,
            evidence);

        Assert.Equal(1, result.KnownSpeakerCandidateCount);
        Assert.Equal(0, result.EligibleSpeakerCandidateCount);
        Assert.Empty(result.EligibleSpeakerCandidateIds);
        Assert.Equal(0, result.EligibleAddresseeCandidateCount);
        Assert.Empty(result.EligibleAddresseeCandidateIds);
        Assert.Equal("141:SPEAKER_13:female:940:1:false|addressee=", result.SpeakerCandidateEvidence);
    }

    [Fact]
    public void Summarize_UnknownEvidenceDoesNotCountAsKnown()
    {
        var cues = new[] { Cue(1, 0, 1, "candidate") };
        var speakers = new Dictionary<int, string?> { [1] = "SPEAKER_X" };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_X"] = new(SpeakerVoiceGender.Unknown, 0.99, 3)
        };

        var result = GenderReviewCoverageDiagnostics.Summarize(
            cues,
            new HashSet<int> { 1 },
            speakers,
            evidence);

        Assert.Equal(0, result.KnownSpeakerCandidateCount);
        Assert.Empty(result.KnownSpeakerCandidateIds);
        Assert.Equal(0, result.KnownAddresseeCandidateCount);
        Assert.Empty(result.KnownAddresseeCandidateIds);
        Assert.Equal(0, result.EligibleAddresseeCandidateCount);
        Assert.Empty(result.EligibleAddresseeCandidateIds);
        Assert.Equal(0, result.KnownRelevantGenderEvidenceCount);
        Assert.Equal(0, result.EligibleSpeakerCandidateCount);
        Assert.Empty(result.EligibleSpeakerCandidateIds);
        Assert.Equal("1:SPEAKER_X:unknown:990:3:false|addressee=", result.SpeakerCandidateEvidence);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
