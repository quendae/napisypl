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
            Cue(4, 4.0, 5.0, "candidate unknown")
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
        Assert.Equal(2, result.KnownRelevantGenderEvidenceCount);
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
        Assert.Equal(0, result.KnownRelevantGenderEvidenceCount);
    }

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
