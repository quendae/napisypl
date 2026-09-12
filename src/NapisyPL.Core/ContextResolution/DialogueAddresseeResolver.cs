using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record DialogueAddresseeResolution(
    string? SpeakerId,
    double Confidence,
    string ReasonCode)
{
    public bool IsResolved => !string.IsNullOrWhiteSpace(SpeakerId);

    public static DialogueAddresseeResolution Unresolved(string reasonCode) =>
        new(null, 0, reasonCode);
}

public static class DialogueAddresseeResolver
{
    private const int SandwichCueDistance = 2;
    private const int ForwardCueDistance = 4;
    private const int LocalCueRadius = 4;
    private const int PartnerHistoryCueCount = 8;
    private static readonly TimeSpan MaxTurnGap = TimeSpan.FromSeconds(8);

    public static string? Resolve(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int cueId) =>
        ResolveDetailed(cues, cueSpeakers, cueId).SpeakerId;

    public static DialogueAddresseeResolution ResolveDetailed(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int cueId)
    {
        var position = FindPosition(cues, cueId);
        if (position < 0)
            return DialogueAddresseeResolution.Unresolved("missing_cue");

        if (!cueSpeakers.TryGetValue(cueId, out var currentSpeaker) ||
            string.IsNullOrWhiteSpace(currentSpeaker))
            return DialogueAddresseeResolution.Unresolved("missing_current_speaker");

        var speaker = currentSpeaker!;

        var previous = FindPreviousOtherSpeaker(
            cues,
            cueSpeakers,
            position,
            speaker,
            SandwichCueDistance,
            MaxTurnGap);
        var next = FindNextOtherSpeaker(
            cues,
            cueSpeakers,
            position,
            speaker,
            SandwichCueDistance,
            MaxTurnGap);

        if (!string.IsNullOrWhiteSpace(previous) &&
            string.Equals(previous, next, StringComparison.Ordinal))
        {
            var sandwichOthers = CollectLocalOtherSpeakers(
                cues,
                cueSpeakers,
                position,
                speaker,
                SandwichCueDistance,
                MaxTurnGap);
            if (sandwichOthers is not null &&
                sandwichOthers.Count == 1 &&
                sandwichOthers.Contains(previous!))
            {
                return new DialogueAddresseeResolution(previous, 0.99, "sandwich_turn");
            }
        }

        var localOthers = CollectLocalOtherSpeakers(
            cues,
            cueSpeakers,
            position,
            speaker,
            LocalCueRadius,
            MaxTurnGap);
        if (localOthers is null)
            return DialogueAddresseeResolution.Unresolved("missing_local_speaker");
        if (localOthers.Count > 1)
            return DialogueAddresseeResolution.Unresolved("third_speaker");

        var nextSpeaker = FindNextOtherSpeaker(
            cues,
            cueSpeakers,
            position,
            speaker,
            ForwardCueDistance,
            MaxTurnGap);
        if (!string.IsNullOrWhiteSpace(nextSpeaker) &&
            localOthers.Count == 1 &&
            localOthers.Contains(nextSpeaker!))
        {
            return new DialogueAddresseeResolution(nextSpeaker, 0.94, "next_turn_two_speaker");
        }

        var persistentPartner = ResolvePersistentPartner(cues, cueSpeakers, position, speaker);
        if (persistentPartner.IsResolved)
            return persistentPartner;

        return persistentPartner.ReasonCode is "third_speaker" or "missing_local_speaker"
            ? persistentPartner
            : DialogueAddresseeResolution.Unresolved("insufficient_turn_evidence");
    }

    private static int FindPosition(IReadOnlyList<SubtitleCue> cues, int cueId)
    {
        for (var i = 0; i < cues.Count; i++)
        {
            if (cues[i].Index == cueId)
                return i;
        }

        return -1;
    }

    private static DialogueAddresseeResolution ResolvePersistentPartner(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int position,
        string currentSpeaker)
    {
        var start = Math.Max(0, position - PartnerHistoryCueCount + 1);
        var chronologicalSpeakers = new List<string>();
        var distinctSpeakers = new HashSet<string>(StringComparer.Ordinal);

        for (var i = position; i >= start; i--)
        {
            if (i < position && GapBetween(cues[i], cues[i + 1]) > MaxTurnGap)
                break;

            if (!cueSpeakers.TryGetValue(cues[i].Index, out var speaker) ||
                string.IsNullOrWhiteSpace(speaker))
                return DialogueAddresseeResolution.Unresolved("missing_local_speaker");

            chronologicalSpeakers.Add(speaker!);
            distinctSpeakers.Add(speaker!);
            if (distinctSpeakers.Count > 2)
                return DialogueAddresseeResolution.Unresolved("third_speaker");
        }

        chronologicalSpeakers.Reverse();
        if (distinctSpeakers.Count != 2 || !distinctSpeakers.Contains(currentSpeaker))
            return DialogueAddresseeResolution.Unresolved("insufficient_partner_history");

        var partner = distinctSpeakers.Single(speaker => !string.Equals(speaker, currentSpeaker, StringComparison.Ordinal));
        var transitions = 0;
        for (var i = 1; i < chronologicalSpeakers.Count; i++)
        {
            if (!string.Equals(chronologicalSpeakers[i - 1], chronologicalSpeakers[i], StringComparison.Ordinal))
                transitions++;
        }

        return transitions >= 2
            ? new DialogueAddresseeResolution(partner, 0.90, "persistent_partner")
            : DialogueAddresseeResolution.Unresolved("insufficient_partner_history");
    }

    private static HashSet<string>? CollectLocalOtherSpeakers(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int position,
        string currentSpeaker,
        int cueRadius,
        TimeSpan maxGap)
    {
        var current = cues[position];
        var speakers = new HashSet<string>(StringComparer.Ordinal);
        var start = Math.Max(0, position - cueRadius);
        var end = Math.Min(cues.Count - 1, position + cueRadius);

        for (var i = start; i <= end; i++)
        {
            if (i == position)
                continue;

            var candidate = cues[i];
            var gap = DistanceFrom(current, candidate);
            if (gap > maxGap)
                continue;

            if (!cueSpeakers.TryGetValue(candidate.Index, out var speaker) ||
                string.IsNullOrWhiteSpace(speaker))
                return null;
            if (!string.Equals(speaker, currentSpeaker, StringComparison.Ordinal))
                speakers.Add(speaker!);
        }

        return speakers;
    }

    private static string? FindPreviousOtherSpeaker(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int position,
        string currentSpeaker,
        int maxCueDistance,
        TimeSpan maxGap)
    {
        var current = cues[position];
        for (var offset = 1; offset <= maxCueDistance && position - offset >= 0; offset++)
        {
            var candidate = cues[position - offset];
            if (DistanceFrom(current, candidate) > maxGap)
                break;

            if (!cueSpeakers.TryGetValue(candidate.Index, out var speaker) ||
                string.IsNullOrWhiteSpace(speaker))
                return null;
            if (!string.Equals(speaker, currentSpeaker, StringComparison.Ordinal))
                return speaker;
        }

        return null;
    }

    private static string? FindNextOtherSpeaker(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int position,
        string currentSpeaker,
        int maxCueDistance,
        TimeSpan maxGap)
    {
        var current = cues[position];
        for (var offset = 1; offset <= maxCueDistance && position + offset < cues.Count; offset++)
        {
            var candidate = cues[position + offset];
            if (DistanceFrom(current, candidate) > maxGap)
                break;

            if (!cueSpeakers.TryGetValue(candidate.Index, out var speaker) ||
                string.IsNullOrWhiteSpace(speaker))
                return null;
            if (!string.Equals(speaker, currentSpeaker, StringComparison.Ordinal))
                return speaker;
        }

        return null;
    }

    private static TimeSpan DistanceFrom(SubtitleCue current, SubtitleCue candidate)
    {
        if (candidate.End <= current.Start)
            return current.Start - candidate.End;
        if (candidate.Start >= current.End)
            return candidate.Start - current.End;
        return TimeSpan.Zero;
    }

    private static TimeSpan GapBetween(SubtitleCue earlier, SubtitleCue later) =>
        later.Start >= earlier.End
            ? later.Start - earlier.End
            : TimeSpan.Zero;
}
