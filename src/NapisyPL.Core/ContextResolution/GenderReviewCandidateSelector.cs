using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public sealed record GenderReviewBatch(
    IReadOnlyList<SubtitleCue> Cues,
    IReadOnlySet<int> CandidateIds);

public static partial class GenderReviewCandidateSelector
{
    private static readonly string[] CommonGenderedWords =
    [
        "gotowy", "gotowa", "gotowi", "gotowe",
        "zmęczony", "zmęczona", "zmęczeni", "zmęczone",
        "pewny", "pewna", "pewni", "pewne",
        "szczęśliwy", "szczęśliwa", "szczęśliwi", "szczęśliwe",
        "sam", "sama", "sami", "same",
        "zły", "zła", "źli", "złe",
        "martwy", "martwa", "martwi", "martwe",
        "wolny", "wolna", "wolni", "wolne",
        "winny", "winna", "winni", "winne",
        "chory", "chora", "chorzy", "chore",
        "urodzony", "urodzona", "urodzeni", "urodzone"
    ];

    public static IReadOnlySet<int> SelectCandidateIds(IReadOnlyList<SubtitleCue> translated)
    {
        var result = new HashSet<int>();
        foreach (var cue in translated)
        {
            if (ContainsGenderSensitiveForm(cue.Text))
                result.Add(cue.Index);
        }
        return result;
    }

    public static IReadOnlyList<GenderReviewBatch> BuildReviewBatches(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlySet<int> candidateIds,
        int radius = 2,
        int maxCandidatesPerBatch = 10,
        int maxContextCuesPerBatch = 50)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (maxCandidatesPerBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxCandidatesPerBatch));
        if (maxContextCuesPerBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxContextCuesPerBatch));
        if (cues.Count == 0 || candidateIds.Count == 0)
            return [];

        var positionById = cues
            .Select((cue, index) => (cue.Index, index))
            .ToDictionary(pair => pair.Index, pair => pair.index);
        var orderedCandidates = candidateIds
            .Where(positionById.ContainsKey)
            .OrderBy(id => positionById[id])
            .ToArray();

        var result = new List<GenderReviewBatch>();
        var batchCandidateIds = new List<int>();
        var batchPositions = new SortedSet<int>();

        void Flush()
        {
            if (batchCandidateIds.Count == 0)
                return;
            result.Add(new GenderReviewBatch(
                batchPositions.Select(position => cues[position]).ToArray(),
                batchCandidateIds.ToHashSet()));
            batchCandidateIds.Clear();
            batchPositions.Clear();
        }

        foreach (var candidateId in orderedCandidates)
        {
            var position = positionById[candidateId];
            var neighbourhood = Enumerable.Range(
                    Math.Max(0, position - radius),
                    Math.Min(cues.Count - 1, position + radius) - Math.Max(0, position - radius) + 1)
                .ToArray();
            var combinedContextCount = batchPositions.Union(neighbourhood).Count();

            if (batchCandidateIds.Count >= maxCandidatesPerBatch ||
                (batchCandidateIds.Count > 0 && combinedContextCount > maxContextCuesPerBatch))
            {
                Flush();
            }

            batchCandidateIds.Add(candidateId);
            foreach (var contextPosition in neighbourhood)
                batchPositions.Add(contextPosition);
        }

        Flush();
        return result;
    }

    public static IReadOnlyList<IReadOnlyList<SubtitleCue>> BuildContextWindows(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlySet<int> candidateIds,
        int radius = 3,
        int maxWindowCues = 14)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (maxWindowCues <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWindowCues));
        if (maxWindowCues < radius * 2 + 1)
            throw new ArgumentOutOfRangeException(nameof(maxWindowCues), "Maximum window size must fit the requested context radius.");
        if (cues.Count == 0 || candidateIds.Count == 0)
            return [];

        var positions = cues
            .Select((cue, index) => (cue.Index, index))
            .Where(pair => candidateIds.Contains(pair.Index))
            .Select(pair => pair.index)
            .OrderBy(index => index)
            .ToArray();

        if (positions.Length == 0)
            return [];

        var ranges = new List<(int Start, int End)>();
        foreach (var position in positions)
        {
            var start = Math.Max(0, position - radius);
            var end = Math.Min(cues.Count - 1, position + radius);

            if (ranges.Count == 0)
            {
                ranges.Add((start, end));
                continue;
            }

            var current = ranges[^1];
            var combinedEnd = Math.Max(current.End, end);
            var canMerge = start <= current.End + 1 && combinedEnd - current.Start + 1 <= maxWindowCues;
            if (canMerge)
                ranges[^1] = (current.Start, combinedEnd);
            else
                ranges.Add((start, end));
        }

        return ranges
            .Select(range => (IReadOnlyList<SubtitleCue>)cues.Skip(range.Start).Take(range.End - range.Start + 1).ToArray())
            .ToArray();
    }

    private static bool ContainsGenderSensitiveForm(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (PastTenseGenderRegex().IsMatch(text))
            return true;

        return CommonGenderedWords.Any(word =>
            Regex.IsMatch(text, $@"(?<!\p{{L}}){Regex.Escape(word)}(?!\p{{L}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    [GeneratedRegex(@"(?<!\p{L})\p{L}*ł(?:em|am|eś|aś|bym|abym|byś|abyś|a|i|y)?(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PastTenseGenderRegex();
}
