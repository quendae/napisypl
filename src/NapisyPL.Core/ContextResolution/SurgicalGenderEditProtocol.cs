using System.Text.Json;
using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

public enum GenderAgreementTarget
{
    Unknown,
    Speaker,
    Addressee
}

public enum SurgicalGenderEditRejectReason
{
    None,
    CueMismatch,
    LowConfidence,
    InvalidFragment,
    Unchanged,
    NotInflectionOnly,
    FindMissing,
    FindAmbiguous
}

public sealed record SurgicalGenderEdit(
    int Id,
    string Find,
    string Replace,
    double Confidence,
    GenderAgreementTarget Target = GenderAgreementTarget.Unknown);

public static class SurgicalGenderEditProtocol
{
    public static IReadOnlyList<SurgicalGenderEdit> ParseResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            throw new InvalidDataException("Gender reviewer returned an empty response.");

        var json = StripFence(response.Trim());
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Gender reviewer response must be a JSON array.");

            var result = new List<SurgicalGenderEdit>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !item.TryGetProperty("id", out var idElement) || !idElement.TryGetInt32(out var id) || id <= 0 ||
                    !item.TryGetProperty("find", out var findElement) || findElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("replace", out var replaceElement) || replaceElement.ValueKind != JsonValueKind.String ||
                    !item.TryGetProperty("confidence", out var confidenceElement) || !confidenceElement.TryGetDouble(out var confidence))
                    throw new InvalidDataException("Gender reviewer returned an invalid surgical-edit entry.");

                var find = findElement.GetString();
                var replace = replaceElement.GetString();
                if (string.IsNullOrWhiteSpace(find) || string.IsNullOrWhiteSpace(replace) || confidence is < 0 or > 1)
                    throw new InvalidDataException("Gender reviewer returned an invalid surgical-edit value.");

                var target = GenderAgreementTarget.Unknown;
                if (item.TryGetProperty("target", out var targetElement))
                {
                    if (targetElement.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("Gender reviewer returned an invalid agreement target.");
                    target = targetElement.GetString()?.ToLowerInvariant() switch
                    {
                        "speaker" => GenderAgreementTarget.Speaker,
                        "addressee" => GenderAgreementTarget.Addressee,
                        _ => throw new InvalidDataException("Gender reviewer returned an unknown agreement target.")
                    };
                }

                result.Add(new SurgicalGenderEdit(id, find, replace, confidence, target));
            }

            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Gender reviewer returned invalid JSON.", ex);
        }
    }

    private static string StripFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
            return text;
        var firstNewLine = text.IndexOf('\n');
        var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? text[(firstNewLine + 1)..lastFence].Trim()
            : text;
    }
}

public static class SurgicalGenderContextGuard
{
    private const double MinimumAddresseeConfidence = 0.95;

    public static bool CanApply(
        SurgicalGenderEdit edit,
        string? currentSpeaker,
        string? probableAddressee)
    {
        if (string.IsNullOrWhiteSpace(currentSpeaker) || edit.Target == GenderAgreementTarget.Unknown)
            return false;

        var inferredTarget = GenderAgreementTargetClassifier.Infer(edit.Find, edit.Replace);
        if (inferredTarget != GenderAgreementTarget.Unknown && inferredTarget != edit.Target)
            return false;

        return edit.Target switch
        {
            GenderAgreementTarget.Speaker => true,
            GenderAgreementTarget.Addressee =>
                edit.Confidence >= MinimumAddresseeConfidence &&
                !string.IsNullOrWhiteSpace(probableAddressee) &&
                !string.Equals(currentSpeaker, probableAddressee, StringComparison.Ordinal),
            _ => false
        };
    }
}

public static partial class GenderAgreementTargetClassifier
{
    private static readonly HashSet<(string Left, string Right)> SpeakerEndingPairs = BuildPairs(
        ("em", "am"),
        ("bym", "abym"),
        ("liśmy", "łyśmy"),
        ("ienem", "nam"));

    private static readonly HashSet<(string Left, string Right)> AddresseeEndingPairs = BuildPairs(
        ("eś", "aś"),
        ("byś", "abyś"),
        ("liście", "łyście"),
        ("ieneś", "naś"));

    public static GenderAgreementTarget Infer(string find, string replace)
    {
        var sourceWords = WordRegex().Matches(find).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var replacementWords = WordRegex().Matches(replace).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (sourceWords.Length == 0 || sourceWords.Length != replacementWords.Length)
            return GenderAgreementTarget.Unknown;

        var inferred = GenderAgreementTarget.Unknown;
        for (var i = 0; i < sourceWords.Length; i++)
        {
            if (string.Equals(sourceWords[i], replacementWords[i], StringComparison.Ordinal))
                continue;

            var prefix = CommonPrefixLength(sourceWords[i], replacementWords[i]);
            var pair = (sourceWords[i][prefix..], replacementWords[i][prefix..]);
            var wordTarget = SpeakerEndingPairs.Contains(pair)
                ? GenderAgreementTarget.Speaker
                : AddresseeEndingPairs.Contains(pair)
                    ? GenderAgreementTarget.Addressee
                    : GenderAgreementTarget.Unknown;

            if (wordTarget == GenderAgreementTarget.Unknown)
                continue;
            if (inferred != GenderAgreementTarget.Unknown && inferred != wordTarget)
                return GenderAgreementTarget.Unknown;
            inferred = wordTarget;
        }

        return inferred;
    }

    private static HashSet<(string Left, string Right)> BuildPairs(params (string Left, string Right)[] pairs)
    {
        var result = new HashSet<(string Left, string Right)>();
        foreach (var pair in pairs)
        {
            result.Add(pair);
            result.Add((pair.Right, pair.Left));
        }
        return result;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
            index++;
        return index;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}

public static partial class SurgicalGenderEditApplier
{
    public const double MinimumConfidence = 0.85;
    private const int MaxFragmentWordsPerSide = 6;
    private const int MaxChangedWords = 3;
    private const int MaxCharsPerSide = 60;

    private static readonly HashSet<(string Left, string Right)> AllowedEndingPairs = BuildAllowedEndingPairs();

    public static bool TryApply(SubtitleCue cue, SurgicalGenderEdit edit, out SubtitleCue changed) =>
        TryApply(cue, edit, out changed, out _);

    public static bool TryApply(
        SubtitleCue cue,
        SurgicalGenderEdit edit,
        out SubtitleCue changed,
        out SurgicalGenderEditRejectReason rejectReason)
    {
        changed = cue;
        rejectReason = SurgicalGenderEditRejectReason.None;

        if (edit.Id != cue.Index)
        {
            rejectReason = SurgicalGenderEditRejectReason.CueMismatch;
            return false;
        }

        if (edit.Confidence < MinimumConfidence)
        {
            rejectReason = SurgicalGenderEditRejectReason.LowConfidence;
            return false;
        }

        if (!IsSmallFragment(edit.Find) || !IsSmallFragment(edit.Replace))
        {
            rejectReason = SurgicalGenderEditRejectReason.InvalidFragment;
            return false;
        }

        if (string.Equals(edit.Find, edit.Replace, StringComparison.Ordinal))
        {
            rejectReason = SurgicalGenderEditRejectReason.Unchanged;
            return false;
        }

        if (!LooksLikeInflectionOnly(edit.Find, edit.Replace))
        {
            rejectReason = SurgicalGenderEditRejectReason.NotInflectionOnly;
            return false;
        }

        var first = cue.Text.IndexOf(edit.Find, StringComparison.Ordinal);
        if (first < 0)
        {
            rejectReason = SurgicalGenderEditRejectReason.FindMissing;
            return false;
        }

        var second = cue.Text.IndexOf(edit.Find, first + edit.Find.Length, StringComparison.Ordinal);
        if (second >= 0)
        {
            rejectReason = SurgicalGenderEditRejectReason.FindAmbiguous;
            return false;
        }

        var updated = cue.Text[..first] + edit.Replace + cue.Text[(first + edit.Find.Length)..];
        changed = cue with { Text = updated };
        return true;
    }

    private static bool IsSmallFragment(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxCharsPerSide || value.Contains('\n') || value.Contains('\r'))
            return false;
        return WordRegex().Matches(value).Count is > 0 and <= MaxFragmentWordsPerSide;
    }

    private static bool LooksLikeInflectionOnly(string find, string replace)
    {
        var sourceWords = WordRegex().Matches(find).Select(match => match.Value.ToLowerInvariant()).ToArray();
        var replacementWords = WordRegex().Matches(replace).Select(match => match.Value.ToLowerInvariant()).ToArray();
        if (sourceWords.Length == 0 || sourceWords.Length != replacementWords.Length)
            return false;

        var changedWordCount = 0;
        for (var i = 0; i < sourceWords.Length; i++)
        {
            var source = sourceWords[i];
            var replacement = replacementWords[i];
            if (string.Equals(source, replacement, StringComparison.Ordinal))
                continue;

            changedWordCount++;
            var shorterLength = Math.Min(source.Length, replacement.Length);
            if (shorterLength < 2)
                return false;

            var commonPrefix = CommonPrefixLength(source, replacement);
            var minimumPrefix = Math.Max(2, shorterLength / 2);
            if (commonPrefix < minimumPrefix)
                return false;

            var sourceEnding = source[commonPrefix..];
            var replacementEnding = replacement[commonPrefix..];
            if (!AllowedEndingPairs.Contains((sourceEnding, replacementEnding)))
                return false;
        }

        return changedWordCount is > 0 and <= MaxChangedWords;
    }

    private static HashSet<(string Left, string Right)> BuildAllowedEndingPairs()
    {
        (string Left, string Right)[] pairs =
        [
            ("", "a"),
            ("y", "a"),
            ("y", "e"),
            ("i", "e"),
            ("i", "y"),
            ("em", "am"),
            ("eś", "aś"),
            ("by", "aby"),
            ("bym", "abym"),
            ("byś", "abyś"),
            ("li", "ły"),
            ("liśmy", "łyśmy"),
            ("liście", "łyście"),
            ("eni", "one"),
            ("ien", "na"),
            ("ienem", "nam"),
            ("ieneś", "naś")
        ];

        var result = new HashSet<(string Left, string Right)>();
        foreach (var pair in pairs)
        {
            result.Add(pair);
            result.Add((pair.Right, pair.Left));
        }
        return result;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = Math.Min(left.Length, right.Length);
        var index = 0;
        while (index < length && left[index] == right[index])
            index++;
        return index;
    }

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}
