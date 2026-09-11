using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public static class DialogueAddresseeResolver
{
    private const int MaxCueDistance = 2;
    private static readonly TimeSpan MaxTurnGap = TimeSpan.FromSeconds(8);

    public static string? Resolve(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int cueId)
    {
        var position = -1;
        for (var i = 0; i < cues.Count; i++)
        {
            if (cues[i].Index == cueId)
            {
                position = i;
                break;
            }
        }

        if (position < 0 ||
            !cueSpeakers.TryGetValue(cueId, out var currentSpeaker) ||
            string.IsNullOrWhiteSpace(currentSpeaker))
            return null;

        var previous = FindPreviousOtherSpeaker(cues, cueSpeakers, position, currentSpeaker!);
        var next = FindNextOtherSpeaker(cues, cueSpeakers, position, currentSpeaker!);

        return previous is not null &&
               next is not null &&
               string.Equals(previous, next, StringComparison.Ordinal)
            ? previous
            : null;
    }

    private static string? FindPreviousOtherSpeaker(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        int position,
        string currentSpeaker)
    {
        var current = cues[position];
        for (var offset = 1; offset <= MaxCueDistance && position - offset >= 0; offset++)
        {
            var candidate = cues[position - offset];
            if (current.Start - candidate.End > MaxTurnGap)
                break;

            if (!cueSpeakers.TryGetValue(candidate.Index, out var speaker) || string.IsNullOrWhiteSpace(speaker))
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
        string currentSpeaker)
    {
        var current = cues[position];
        for (var offset = 1; offset <= MaxCueDistance && position + offset < cues.Count; offset++)
        {
            var candidate = cues[position + offset];
            if (candidate.Start - current.End > MaxTurnGap)
                break;

            if (!cueSpeakers.TryGetValue(candidate.Index, out var speaker) || string.IsNullOrWhiteSpace(speaker))
                return null;
            if (!string.Equals(speaker, currentSpeaker, StringComparison.Ordinal))
                return speaker;
        }

        return null;
    }
}
