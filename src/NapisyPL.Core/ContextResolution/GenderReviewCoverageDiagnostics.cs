using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record GenderReviewCoverageSummary(
    int CandidateCount,
    int KnownSpeakerCandidateCount,
    IReadOnlyList<int> KnownSpeakerCandidateIds,
    int KnownAddresseeCandidateCount,
    IReadOnlyList<int> KnownAddresseeCandidateIds,
    int KnownRelevantGenderEvidenceCount);

public static class GenderReviewCoverageDiagnostics
{
    public static GenderReviewCoverageSummary Summarize(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlySet<int> candidateIds,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence)
    {
        var knownSpeakerCandidates = new List<int>();
        var knownAddresseeCandidates = new List<int>();
        var relevantKnownSpeakers = new HashSet<string>(StringComparer.Ordinal);

        foreach (var candidateId in candidateIds.OrderBy(id => id))
        {
            if (cueSpeakers.TryGetValue(candidateId, out var speaker) &&
                IsKnown(speaker, speakerGenderEvidence))
            {
                knownSpeakerCandidates.Add(candidateId);
                relevantKnownSpeakers.Add(speaker!);
            }

            var addressee = DialogueAddresseeResolver.Resolve(source, cueSpeakers, candidateId);
            if (IsKnown(addressee, speakerGenderEvidence))
            {
                knownAddresseeCandidates.Add(candidateId);
                relevantKnownSpeakers.Add(addressee!);
            }
        }

        return new GenderReviewCoverageSummary(
            candidateIds.Count,
            knownSpeakerCandidates.Count,
            knownSpeakerCandidates,
            knownAddresseeCandidates.Count,
            knownAddresseeCandidates,
            relevantKnownSpeakers.Count);
    }

    private static bool IsKnown(
        string? speaker,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence) =>
        !string.IsNullOrWhiteSpace(speaker) &&
        speakerGenderEvidence.TryGetValue(speaker, out var evidence) &&
        evidence.Gender != SpeakerVoiceGender.Unknown;
}
