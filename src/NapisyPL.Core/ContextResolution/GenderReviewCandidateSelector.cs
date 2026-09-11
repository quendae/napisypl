using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

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

    public static IReadOnlyList<IReadOnlyList<SubtitleCue>> BuildContextWindows(
        IReadOnlyList<SubtitleCue> cues,
        IReadOnlySet<int> candidateIds,
        int radius = 3)
    {
        if (radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius));
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
            if (ranges.Count == 0 || start > ranges[^1].End + 1)
                ranges.Add((start, end));
            else
                ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
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

    // Polish past/conditional forms carrying grammatical gender, e.g. zrobiłem/zrobiłam,
    // zrobiłeś/zrobiłaś, zrobił/zrobiła, zrobiłbym/zrobiłabym, zrobili/zrobiły.
    [GeneratedRegex(@"(?<!\p{L})\p{L}*ł(?:em|am|eś|aś|bym|abym|byś|abyś|a|i|y)?(?!\p{L})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PastTenseGenderRegex();
}
