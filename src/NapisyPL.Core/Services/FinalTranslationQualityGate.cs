using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed record TranslationQualityIssue(
    int CueId,
    string Reason,
    int SourceWordCount,
    int OutputWordCount);

public static partial class FinalTranslationQualityGate
{
    private const string DegenerateSymbols = "*#~|=_♪";

    public static IReadOnlyList<TranslationQualityIssue> Evaluate(
        IReadOnlyList<SubtitleCue> sourceCues,
        IReadOnlyList<SubtitleCue> translatedCues)
    {
        ArgumentNullException.ThrowIfNull(sourceCues);
        ArgumentNullException.ThrowIfNull(translatedCues);

        var translatedById = translatedCues.ToDictionary(cue => cue.Index);
        var issues = new List<TranslationQualityIssue>();

        foreach (var source in sourceCues)
        {
            if (!translatedById.TryGetValue(source.Index, out var translated))
                throw new InvalidDataException($"Quality gate: missing translated cue {source.Index}.");

            var reason = Detect(source.Text, translated.Text);
            if (reason is null)
                continue;

            issues.Add(new TranslationQualityIssue(
                source.Index,
                reason,
                WordTokens(source.Text).Length,
                WordTokens(translated.Text).Length));
        }

        return issues;
    }

    public static string? Detect(string? sourceText, string? translatedText)
    {
        sourceText ??= string.Empty;
        translatedText ??= string.Empty;

        if (!string.IsNullOrWhiteSpace(sourceText) && string.IsNullOrWhiteSpace(translatedText))
            return "empty_output";

        foreach (var symbol in DegenerateSymbols)
        {
            var outputCount = translatedText.Count(ch => ch == symbol);
            var sourceCount = sourceText.Count(ch => ch == symbol);
            if (outputCount >= 24 && outputCount >= sourceCount + 16)
                return "symbol_run";
        }

        var outputNumbers = NumberRegex().Matches(translatedText)
            .Select(match => int.TryParse(match.Value, out var value) ? value : int.MinValue)
            .Where(value => value != int.MinValue)
            .ToArray();
        var sourceNumberCount = NumberRegex().Matches(sourceText).Count;
        if (outputNumbers.Length >= 12 &&
            outputNumbers.Length >= sourceNumberCount + 8 &&
            LongestConsecutiveNumericRun(outputNumbers) >= 12)
        {
            return "numeric_run";
        }

        var sourceWords = WordTokens(sourceText);
        var outputWords = WordTokens(translatedText);
        if (outputWords.Length >= 24)
        {
            var dominantCount = outputWords
                .GroupBy(word => word, StringComparer.OrdinalIgnoreCase)
                .Max(group => group.Count());
            var dominantRatio = (double)dominantCount / outputWords.Length;
            if (dominantCount >= 16 &&
                dominantRatio >= 0.65 &&
                outputWords.Length > (sourceWords.Length * 3) + 12)
            {
                return "dominant_token";
            }
        }

        if (HasRepeatedNgramLoop(outputWords, sourceWords.Length))
            return "repeated_ngram";

        if (outputWords.Length > Math.Max(96, (sourceWords.Length * 8) + 32))
            return "length_explosion";

        return null;
    }

    private static string[] WordTokens(string text) =>
        WordRegex().Matches(text)
            .Select(match => match.Value.ToLowerInvariant())
            .ToArray();

    private static bool HasRepeatedNgramLoop(IReadOnlyList<string> words, int sourceWordCount)
    {
        if (words.Count < 12 || words.Count <= (sourceWordCount * 3) + 12)
            return false;

        for (var ngramSize = 2; ngramSize <= 5; ngramSize++)
        {
            for (var start = 0; start + (ngramSize * 6) <= words.Count; start++)
            {
                var repeats = 1;
                while (start + ((repeats + 1) * ngramSize) <= words.Count &&
                       NgramEquals(words, start, start + (repeats * ngramSize), ngramSize))
                {
                    repeats++;
                }

                if (repeats >= 6)
                    return true;
            }
        }

        return false;
    }

    private static bool NgramEquals(
        IReadOnlyList<string> words,
        int firstStart,
        int secondStart,
        int length)
    {
        for (var offset = 0; offset < length; offset++)
        {
            if (!string.Equals(
                    words[firstStart + offset],
                    words[secondStart + offset],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static int LongestConsecutiveNumericRun(IReadOnlyList<int> values)
    {
        if (values.Count == 0)
            return 0;

        var best = 1;
        var current = 1;
        for (var index = 1; index < values.Count; index++)
        {
            if (values[index] == values[index - 1] + 1)
            {
                current++;
                best = Math.Max(best, current);
            }
            else
            {
                current = 1;
            }
        }

        return best;
    }

    [GeneratedRegex(@"[\p{L}\p{M}\p{N}_']+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"(?<!\w)\d+(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex NumberRegex();
}
