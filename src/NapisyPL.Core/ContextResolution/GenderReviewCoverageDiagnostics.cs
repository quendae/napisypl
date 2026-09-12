using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record GenderReviewCoverageSummary(
    int CandidateCount,
    int KnownSpeakerCandidateCount,
    IReadOnlyList<int> KnownSpeakerCandidateIds,
    int KnownAddresseeCandidateCount,
    IReadOnlyList<int> KnownAddresseeCandidateIds,
    int KnownRelevantGenderEvidenceCount,
    int EligibleSpeakerCandidateCount,
    IReadOnlyList<int> EligibleSpeakerCandidateIds,
    string SpeakerCandidateEvidence);

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
        var eligibleSpeakerCandidates = new List<int>();
        var relevantKnownSpeakers = new HashSet<string>(StringComparer.Ordinal);
        var evidenceEntries = new List<string>();
        var addresseeEntries = new List<string>();

        foreach (var candidateId in candidateIds.OrderBy(id => id))
        {
            cueSpeakers.TryGetValue(candidateId, out var speaker);
            SpeakerGenderEvidence? ownEvidence = null;
            if (!string.IsNullOrWhiteSpace(speaker))
                speakerGenderEvidence.TryGetValue(speaker!, out ownEvidence);

            if (IsKnown(ownEvidence))
            {
                knownSpeakerCandidates.Add(candidateId);
                relevantKnownSpeakers.Add(speaker!);
            }

            if (SpeakerGenderReviewEligibility.IsEligible(ownEvidence))
                eligibleSpeakerCandidates.Add(candidateId);

            evidenceEntries.Add(FormatCandidateEvidence(candidateId, speaker, ownEvidence));

            var addresseeResolution = DialogueAddresseeResolver.ResolveDetailed(source, cueSpeakers, candidateId);
            if (!addresseeResolution.IsResolved)
                continue;

            SpeakerGenderEvidence? addresseeEvidence = null;
            speakerGenderEvidence.TryGetValue(addresseeResolution.SpeakerId!, out addresseeEvidence);
            addresseeEntries.Add(FormatAddresseeEvidence(candidateId, addresseeResolution, addresseeEvidence));

            if (IsKnown(addresseeEvidence))
            {
                knownAddresseeCandidates.Add(candidateId);
                relevantKnownSpeakers.Add(addresseeResolution.SpeakerId!);
            }
        }

        var combinedEvidence = string.Join(";", evidenceEntries) +
                               "|addressee=" + string.Join(";", addresseeEntries);

        return new GenderReviewCoverageSummary(
            candidateIds.Count,
            knownSpeakerCandidates.Count,
            knownSpeakerCandidates,
            knownAddresseeCandidates.Count,
            knownAddresseeCandidates,
            relevantKnownSpeakers.Count,
            eligibleSpeakerCandidates.Count,
            eligibleSpeakerCandidates,
            combinedEvidence);
    }

    private static string FormatCandidateEvidence(
        int candidateId,
        string? speaker,
        SpeakerGenderEvidence? evidence)
    {
        var speakerId = string.IsNullOrWhiteSpace(speaker) ? "-" : speaker;
        var gender = evidence?.Gender.ToString().ToLowerInvariant() ?? "unknown";
        var confidencePermille = evidence is null
            ? 0
            : SpeakerGenderObservationDiagnostics.ToPermille(evidence.Confidence);
        var sampleCount = evidence?.SampleCount ?? 0;
        var eligible = SpeakerGenderReviewEligibility.IsEligible(evidence)
            ? "true"
            : "false";

        return $"{candidateId}:{speakerId}:{gender}:{confidencePermille}:{sampleCount}:{eligible}";
    }

    private static string FormatAddresseeEvidence(
        int candidateId,
        DialogueAddresseeResolution resolution,
        SpeakerGenderEvidence? evidence)
    {
        var gender = evidence?.Gender.ToString().ToLowerInvariant() ?? "unknown";
        var genderConfidencePermille = evidence is null
            ? 0
            : SpeakerGenderObservationDiagnostics.ToPermille(evidence.Confidence);
        var sampleCount = evidence?.SampleCount ?? 0;
        var eligible = SpeakerGenderReviewEligibility.IsEligible(evidence)
            ? "true"
            : "false";
        var resolverConfidencePermille = SpeakerGenderObservationDiagnostics.ToPermille(resolution.Confidence);

        return $"{candidateId}:{resolution.SpeakerId}:{resolverConfidencePermille}:{resolution.ReasonCode}:{gender}:{genderConfidencePermille}:{sampleCount}:{eligible}";
    }

    private static bool IsKnown(SpeakerGenderEvidence? evidence) =>
        evidence is not null && evidence.Gender != SpeakerVoiceGender.Unknown;
}
