using System.Text.RegularExpressions;

namespace NapisyPL.Core.ContextResolution;

/// <summary>
/// The English line often names the person it speaks to ("But you have, Jaclyn."). That name
/// settles the addressee's gender inside the cue itself, where the audio cannot help: the
/// addressee is silent while the line is spoken, so pitch describes the wrong person.
/// Chance S01E10 #580 came out "Przeżyłeś" for Jaclyn because of exactly that.
/// </summary>
public static partial class VocativeAddresseeEvidence
{
    /// <summary>Titles carry the gender themselves, whatever surname follows.</summary>
    private static readonly Dictionary<string, SpeakerVoiceGender> Titles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mr"] = SpeakerVoiceGender.Male,
        ["mister"] = SpeakerVoiceGender.Male,
        ["sir"] = SpeakerVoiceGender.Male,
        ["lord"] = SpeakerVoiceGender.Male,
        ["mrs"] = SpeakerVoiceGender.Female,
        ["ms"] = SpeakerVoiceGender.Female,
        ["miss"] = SpeakerVoiceGender.Female,
        ["madam"] = SpeakerVoiceGender.Female,
        ["maam"] = SpeakerVoiceGender.Female,
        ["lady"] = SpeakerVoiceGender.Female
    };

    /// <summary>
    /// Capitalised words that open or close a clause without naming anyone. Without this list
    /// "Sure, you did" and "You did, Honey" would be read as vocative names, and some of them
    /// do sit in the first-name lexicon.
    /// </summary>
    private static readonly HashSet<string> NotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "well", "yes", "yeah", "yep", "no", "nope", "okay", "ok", "sure", "right", "look", "listen",
        "please", "sorry", "thanks", "thank", "hey", "hi", "hello", "wait", "stop", "come", "now",
        "then", "first", "next", "actually", "honestly", "seriously", "maybe", "perhaps", "however",
        "anyway", "anyhow", "again", "still", "also", "besides", "meanwhile", "suddenly", "finally",
        "true", "false", "good", "great", "fine", "nice", "oh", "ah", "um", "uh", "so", "but", "and",
        "or", "because", "though", "although", "instead", "otherwise", "indeed", "really", "too",
        "either", "neither", "never", "always", "today", "tonight", "tomorrow", "yesterday", "later",
        "sometimes", "usually", "obviously", "clearly", "apparently", "basically", "certainly",
        "honey", "baby", "sweetie", "darling", "dear", "kid", "kiddo", "buddy", "pal", "guys", "everyone",
        // Abbreviations whose full stop otherwise looks like the end of a sentence.
        "mr", "mrs", "ms", "dr", "prof", "st", "rev", "sgt", "lt", "capt", "sr", "jr", "mt"
    };

    /// <summary>
    /// The gender of the person this English line addresses by name, or <c>Unknown</c> when it
    /// names nobody, names somebody the lexicon does not know, or names two people at odds.
    /// </summary>
    public static SpeakerVoiceGender Resolve(string englishText) => Resolve(englishText, out _);

    public static SpeakerVoiceGender Resolve(string englishText, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(englishText))
            return SpeakerVoiceGender.Unknown;

        var found = SpeakerVoiceGender.Unknown;
        var lines = englishText.Replace("\r", string.Empty).Split('\n').Select(Clean).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            var text = lines[index];
            if (text.Length == 0)
                continue;

            // "…, / Jaclyn." is one sentence wrapped over two lines; "recommended -- / Suzanne."
            // is an afterthought naming a third person, and so is a bare "Suzanne." line.
            var continuesVocative = index > 0 && lines[index - 1].EndsWith(',');
            foreach (Match match in VocativeRegex().Matches(text))
            {
                if (!TryReadVocative(text, match, continuesVocative, out var gender, out var candidate))
                    continue;
                if (found != SpeakerVoiceGender.Unknown && found != gender)
                    return SpeakerVoiceGender.Unknown;
                found = gender;
                name = candidate;
            }
        }

        return found;
    }

    private static bool TryReadVocative(
        string text,
        Match match,
        bool continuesVocative,
        out SpeakerVoiceGender gender,
        out string name)
    {
        gender = SpeakerVoiceGender.Unknown;
        name = match.Groups["name"].Value;
        var title = match.Groups["title"].Value.TrimEnd('.');

        if (title.Length == 0 && (name.Length < 2 || NotNames.Contains(name)))
            return false;

        // A cue that is nothing but the name may as well be an answer to "who did it?".
        var before = text[..match.Index];
        var after = text[(match.Index + match.Length)..];
        if (!continuesVocative && !WordRegex().IsMatch(before) && !WordRegex().IsMatch(after))
            return false;

        // A name in a list ("Mary, John, and Peter") addresses nobody.
        if (ContinuesList(after) || EndsWithName(before))
            return false;

        // "This is my sister, Mary." introduces a third person instead of addressing her,
        // and so does "Detective Baxter, my partner, was shot."
        if (AppositiveLeadRegex().IsMatch(before) || AppositiveTailRegex().IsMatch(after))
            return false;

        // "Mrs. Cooper" and "Father Brown" carry the gender in the title; "Dr. Clark" does
        // not, and only earns the surname a lookup because the title was matched as one.
        // A bare "Sir" or "Ma'am" is the whole vocative.
        var lead = title.Length > 0 ? title : name;
        gender = Titles.TryGetValue(lead.Replace("'", string.Empty), out var titleGender)
            ? titleGender
            : SpeakerLabelEvidence.GenderOfLabel(lead) is var roleGender && roleGender != SpeakerVoiceGender.Unknown && title.Length > 0
                ? roleGender
                : SpeakerLabelEvidence.GenderOfLabel(name);
        return gender != SpeakerVoiceGender.Unknown;
    }

    /// <summary>"Mary, John, …" — a comma followed by a conjunction or by another known name.</summary>
    private static bool ContinuesList(string after)
    {
        var match = NextWordRegex().Match(after);
        if (!match.Success)
            return false;
        var word = match.Groups["word"].Value;
        return word is "and" or "or" ||
               (!string.Equals(word, "I", StringComparison.Ordinal) && IsKnownName(word));
    }

    /// <summary>"…, Mary, Jaclyn" — the name before this one makes both of them a list.</summary>
    private static bool EndsWithName(string before)
    {
        var match = PreviousWordRegex().Match(before);
        return match.Success && IsKnownName(match.Groups["word"].Value);
    }

    private static bool IsKnownName(string word) =>
        word.Length > 1 &&
        char.IsUpper(word[0]) &&
        !NotNames.Contains(word) &&
        SpeakerLabelEvidence.GenderOfLabel(word) != SpeakerVoiceGender.Unknown;

    private static string Clean(string line) =>
        NoiseRegex().Replace(line, " ").Replace('’', '\'').Trim();

    /// <summary>Sound tags, dialogue dashes and speaker labels are not part of the sentence.</summary>
    [GeneratedRegex(@"^\s*-\s*|\[[^\]]*\]|\([^)]*\)|^[^\p{Ll}\n]{2,}:|<[^>]*>")]
    private static partial Regex NoiseRegex();

    /// <summary>
    /// A name set off by commas: at the head of a sentence ("Jaclyn, you survived"), at its
    /// tail ("You survived, Jaclyn.") or between them ("Look, Jaclyn, you survived").
    /// </summary>
    [GeneratedRegex(@"(?:^|(?<=[.!?…,;]\s{0,2}))(?:(?<title>Mr\.|Mrs\.|Ms\.|Miss|Madam|Ma'am|Sir|Dr\.|Doctor|Prof\.|Professor|St\.|Rev\.|Father|Sister|Nurse|Officer|Detective|Sergeant|Captain|Agent|Judge|Coach|Chief|President|Uncle|Aunt|Grandma|Grandpa)\s+(?<name>\p{Lu}[\p{L}'-]+)|(?<!\b(?:Mr|Mrs|Ms|Dr|Prof|St|Rev|Sgt|Lt|Capt|Sr|Jr|No|Mt)\.\s{0,2})(?<name>\p{Lu}[\p{L}'-]+))\s*(?=[,.!?…]|$)")]
    private static partial Regex VocativeRegex();

    [GeneratedRegex(@"^\s*,\s*(?:my|your|his|her|our|their|the)\s+[\p{L}'-]+\s*,")]
    private static partial Regex AppositiveTailRegex();

    [GeneratedRegex(@"^\s*,\s*(?<word>[\p{L}'-]+)")]
    private static partial Regex NextWordRegex();

    [GeneratedRegex(@"(?<word>[\p{L}'-]+)\s*,\s*$")]
    private static partial Regex PreviousWordRegex();

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"\b(?:is|was|are|were|meet|it's|that's|this's)\s+(?:my|your|his|her|our|their|the|a|an)?\s*[\p{L}'-]*\s*,?\s*$")]
    private static partial Regex AppositiveLeadRegex();
}
