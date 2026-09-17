using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

/// <summary>What the speaker labels in SDH subtitles ("JACLYN:", "Man:") say about each cue.</summary>
public sealed record SpeakerLabelAnalysis(
    IReadOnlyDictionary<int, SpeakerVoiceGender> CueGender,
    IReadOnlyDictionary<int, string> CueLabel,
    IReadOnlyDictionary<int, SpeakerVoiceGender>? ContinuedGender = null)
{
    public static SpeakerLabelAnalysis Empty { get; } = new(
        new Dictionary<int, SpeakerVoiceGender>(),
        new Dictionary<int, string>());

    /// <summary>The labelled cues and the lines that carry on from them, as one lookup.</summary>
    public IReadOnlyDictionary<int, SpeakerVoiceGender> AllCueGender =>
        ContinuedGender is not { Count: > 0 }
            ? CueGender
            : CueGender.Concat(ContinuedGender.Where(pair => !CueGender.ContainsKey(pair.Key)))
                .ToDictionary(pair => pair.Key, pair => pair.Value);
}

/// <summary>
/// Hearing-impaired subtitles name the speaker. The text says who talks, which is stronger
/// than pitch: voice-overs, letters read aloud and diarization clusters that merged two
/// people are exactly where audio fails (Chance S01E06 #665, S01E10 #545).
/// </summary>
public static partial class SpeakerLabelEvidence
{
    private const string ResourceName = "first_names.ssa.tsv.gz";
    private const int MinimumAgreeingPitchCues = 2;
    private const int MinimumVotedLabelCues = 3;

    /// <summary>Named cues needed before the name speaks for the whole diarized cluster.</summary>
    private const int MinimumClusterLabelCues = 2;

    /// <summary>A named cluster is as certain as the label itself.</summary>
    private const double LabelledProfileConfidence = 0.99;

    /// <summary>A line inheriting the label above it, rather than carrying one.</summary>
    private const double ContinuedLabelConfidence = 0.95;

    private static readonly Lazy<IReadOnlyDictionary<string, SpeakerVoiceGender>> FirstNames = new(LoadFirstNames);

    private static readonly Dictionary<string, SpeakerVoiceGender> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["man"] = SpeakerVoiceGender.Male, ["men"] = SpeakerVoiceGender.Male, ["boy"] = SpeakerVoiceGender.Male,
        ["guy"] = SpeakerVoiceGender.Male, ["father"] = SpeakerVoiceGender.Male, ["dad"] = SpeakerVoiceGender.Male,
        ["husband"] = SpeakerVoiceGender.Male, ["son"] = SpeakerVoiceGender.Male, ["brother"] = SpeakerVoiceGender.Male,
        ["uncle"] = SpeakerVoiceGender.Male, ["grandpa"] = SpeakerVoiceGender.Male, ["king"] = SpeakerVoiceGender.Male,
        ["prince"] = SpeakerVoiceGender.Male, ["lord"] = SpeakerVoiceGender.Male, ["sir"] = SpeakerVoiceGender.Male,
        ["mr"] = SpeakerVoiceGender.Male, ["waiter"] = SpeakerVoiceGender.Male, ["policeman"] = SpeakerVoiceGender.Male,
        ["gentleman"] = SpeakerVoiceGender.Male, ["priest"] = SpeakerVoiceGender.Male,
        ["woman"] = SpeakerVoiceGender.Female, ["women"] = SpeakerVoiceGender.Female, ["girl"] = SpeakerVoiceGender.Female,
        ["mother"] = SpeakerVoiceGender.Female, ["mom"] = SpeakerVoiceGender.Female, ["mum"] = SpeakerVoiceGender.Female,
        ["wife"] = SpeakerVoiceGender.Female, ["daughter"] = SpeakerVoiceGender.Female, ["sister"] = SpeakerVoiceGender.Female,
        ["aunt"] = SpeakerVoiceGender.Female, ["grandma"] = SpeakerVoiceGender.Female, ["queen"] = SpeakerVoiceGender.Female,
        ["princess"] = SpeakerVoiceGender.Female, ["lady"] = SpeakerVoiceGender.Female, ["mrs"] = SpeakerVoiceGender.Female,
        ["ms"] = SpeakerVoiceGender.Female, ["miss"] = SpeakerVoiceGender.Female, ["madam"] = SpeakerVoiceGender.Female,
        ["waitress"] = SpeakerVoiceGender.Female, ["actress"] = SpeakerVoiceGender.Female, ["nun"] = SpeakerVoiceGender.Female
    };

    /// <summary>
    /// Short forms the birth register calls male only because the full male name was common a
    /// century ago. On screen "Sam" is as often Samantha, "Alex" Alexandra, "Jess" Jessica.
    /// The voice decides for these instead of the list.
    /// </summary>
    private static readonly HashSet<string> AmbiguousNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "sam", "sammy", "alex", "jess", "jesse", "sasha", "jean", "lou", "mel", "bo", "nico"
    };

    /// <summary>Labels that look like "Word:" but name no speaker.</summary>
    private static readonly HashSet<string> NotSpeakers = new(StringComparer.OrdinalIgnoreCase)
    {
        "note", "warning", "subtitles", "sync", "translation", "caption", "captions", "ps", "re", "fw", "chapter", "part",
        "episode", "season", "location", "time", "date", "exhibit", "rule", "step", "question", "answer", "q", "a"
    };

    public static SpeakerLabelAnalysis Analyze(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence>? cueGenderEvidence)
    {
        ArgumentNullException.ThrowIfNull(source);

        var labels = new Dictionary<int, string>();
        foreach (var cue in source)
        {
            if (TryGetSingleSpeakerLabel(cue.Text, out var label))
                labels[cue.Index] = label;
        }

        if (labels.Count == 0)
            return SpeakerLabelAnalysis.Empty;

        var genders = new Dictionary<int, SpeakerVoiceGender>();
        var evidence = cueGenderEvidence ?? new Dictionary<int, CueVoiceGenderEvidence>();
        foreach (var group in labels.GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase))
        {
            var cueIds = group.Select(pair => pair.Key).ToArray();
            var male = cueIds.Count(id => ForcedPitch(evidence, id) == SpeakerVoiceGender.Male);
            var female = cueIds.Count(id => ForcedPitch(evidence, id) == SpeakerVoiceGender.Female);

            var gender = GenderOfLabel(group.Key);
            var votedByVoice = gender == SpeakerVoiceGender.Unknown;
            if (votedByVoice)
            {
                if (cueIds.Length < MinimumVotedLabelCues)
                    continue;

                // A surname or nickname ("Blackstone", "D"): the same label's voice decides.
                gender = male >= MinimumAgreeingPitchCues && female == 0 ? SpeakerVoiceGender.Male
                    : female >= MinimumAgreeingPitchCues && male == 0 ? SpeakerVoiceGender.Female
                    : SpeakerVoiceGender.Unknown;
            }
            else
            {
                // "Chance" is a man here, but a name can belong to anyone: only a voice that
                // clearly and repeatedly says otherwise overrules the name list.
                var agreeing = gender == SpeakerVoiceGender.Male ? male : female;
                var opposite = gender == SpeakerVoiceGender.Male ? female : male;
                if (opposite >= 3 && agreeing == 0)
                    gender = SpeakerVoiceGender.Unknown;
            }

            if (gender == SpeakerVoiceGender.Unknown)
                continue;
            foreach (var id in cueIds)
            {
                // "Listen:" is not a person; a voted label never overrules the cue's own voice.
                if (votedByVoice && ForcedPitch(evidence, id) is var pitch && pitch != SpeakerVoiceGender.Unknown && pitch != gender)
                    continue;
                genders[id] = gender;
            }
        }

        return new SpeakerLabelAnalysis(genders, labels);
    }

    /// <summary>A label names the speaker until the dialogue moves on; this far at most.</summary>
    private const int MaximumContinuedCues = 6;

    private static readonly TimeSpan MaximumLabelContinuationGap = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Hearing-impaired subtitles write the name when the speaker changes, not on every line,
    /// so "JACLYN:" owns the lines that follow it as well. The name carries on while the
    /// dialogue does not move on: no new label, no pause, no dash starting another turn, and
    /// the same voice throughout, with the cue's own pitch free to stop it.
    /// </summary>
    public static SpeakerLabelAnalysis Continue(
        SpeakerLabelAnalysis labels,
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence)
    {
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(source);

        if (labels.CueGender.Count == 0)
            return labels;

        var positions = new Dictionary<int, int>(source.Count);
        for (var index = 0; index < source.Count; index++)
            positions[source[index].Index] = index;

        var continued = new Dictionary<int, SpeakerVoiceGender>();
        foreach (var (cueId, gender) in labels.CueGender)
        {
            if (!positions.TryGetValue(cueId, out var start))
                continue;
            cueSpeakers.TryGetValue(cueId, out var owner);
            if (string.IsNullOrWhiteSpace(owner))
                continue;

            for (var at = start + 1; at < source.Count && at - start <= MaximumContinuedCues; at++)
            {
                var cue = source[at];
                if (labels.CueLabel.ContainsKey(cue.Index) || StartsAnotherTurn(cue.Text))
                    break;
                if (cue.Start - source[at - 1].End > MaximumLabelContinuationGap)
                    break;
                if (!cueSpeakers.TryGetValue(cue.Index, out var here) ||
                    !string.Equals(here, owner, StringComparison.Ordinal))
                    break;
                if (ForcedPitch(cueGenderEvidence, cue.Index) is var pitch &&
                    pitch != SpeakerVoiceGender.Unknown && pitch != gender)
                    break;

                continued[cue.Index] = gender;
            }
        }

        return labels with { ContinuedGender = continued };
    }

    /// <summary>A dialogue dash hands the line to somebody else.</summary>
    private static bool StartsAnotherTurn(string text) =>
        text.TrimStart().StartsWith('-') || text.Contains("\n-", StringComparison.Ordinal);

    /// <summary>
    /// Labelled cues become forced cue evidence, so turn resolution (who is addressed)
    /// uses them too. A cluster the labels name repeatedly takes that name's gender for all
    /// of its lines; one the labels show to be two people is dropped instead.
    /// </summary>
    public static (IReadOnlyDictionary<int, CueVoiceGenderEvidence> CueEvidence,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> SpeakerEvidence) Apply(
        SpeakerLabelAnalysis labels,
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueEvidence)
    {
        if (labels.CueGender.Count == 0)
            return (cueEvidence, speakerEvidence);

        var durations = source.ToDictionary(cue => cue.Index, cue => (cue.End - cue.Start).TotalSeconds);
        var cues = new Dictionary<int, CueVoiceGenderEvidence>(cueEvidence);
        foreach (var (cueId, gender) in labels.CueGender)
        {
            var duration = Math.Max(durations.GetValueOrDefault(cueId), 1.0);
            cues[cueId] = new CueVoiceGenderEvidence(gender, 0.99, 1.0, duration, gender, 0.99);
        }

        // A line that only carries on from the label is a shade weaker than the label itself,
        // and it never votes on who the whole cluster is.
        foreach (var (cueId, gender) in labels.ContinuedGender ?? new Dictionary<int, SpeakerVoiceGender>())
        {
            if (labels.CueGender.ContainsKey(cueId))
                continue;
            var duration = Math.Max(durations.GetValueOrDefault(cueId), 1.0);
            cues[cueId] = new CueVoiceGenderEvidence(gender, ContinuedLabelConfidence, 1.0, duration, gender, ContinuedLabelConfidence);
        }

        var speakers = new Dictionary<string, SpeakerGenderEvidence>(speakerEvidence);
        foreach (var (speaker, labelled) in GroupLabelsBySpeaker(labels, cueSpeakers))
        {
            var male = labelled.Count(gender => gender == SpeakerVoiceGender.Male);
            var female = labelled.Count(gender => gender == SpeakerVoiceGender.Female);
            speakerEvidence.TryGetValue(speaker, out var profile);

            // The labels name the same person often enough to stand for the whole voice:
            // every line of this cluster is theirs, not only the ones carrying the label.
            if (male >= MinimumClusterLabelCues && female == 0)
            {
                speakers[speaker] = NameProfile(SpeakerVoiceGender.Male, male, profile);
                continue;
            }

            if (female >= MinimumClusterLabelCues && male == 0)
            {
                speakers[speaker] = NameProfile(SpeakerVoiceGender.Female, female, profile);
                continue;
            }

            // Two names of opposite gender in one cluster: diarization merged two people.
            if (profile is null || profile.Gender == SpeakerVoiceGender.Unknown)
                continue;
            var agreeing = profile.Gender == SpeakerVoiceGender.Male ? male : female;
            var opposite = labelled.Count - agreeing;
            if (opposite >= 2 && opposite >= agreeing)
                speakers[speaker] = new SpeakerGenderEvidence(SpeakerVoiceGender.Unknown, 0, profile.SampleCount);
        }

        return (cues, speakers);
    }

    private static SpeakerGenderEvidence NameProfile(SpeakerVoiceGender gender, int labelledCues, SpeakerGenderEvidence? profile) =>
        new(gender, LabelledProfileConfidence, Math.Max(profile?.SampleCount ?? 0, labelledCues));

    private static Dictionary<string, List<SpeakerVoiceGender>> GroupLabelsBySpeaker(
        SpeakerLabelAnalysis labels,
        IReadOnlyDictionary<int, string?> cueSpeakers)
    {
        var grouped = new Dictionary<string, List<SpeakerVoiceGender>>(StringComparer.Ordinal);
        foreach (var (cueId, gender) in labels.CueGender)
        {
            if (!cueSpeakers.TryGetValue(cueId, out var speaker) || string.IsNullOrWhiteSpace(speaker))
                continue;
            if (!grouped.TryGetValue(speaker!, out var list))
                grouped[speaker!] = list = [];
            list.Add(gender);
        }

        return grouped;
    }

    public static SpeakerVoiceGender GenderOfLabel(string label)
    {
        var words = WordRegex().Matches(label).Select(match => match.Value).ToArray();
        if (words.Length == 0)
            return SpeakerVoiceGender.Unknown;

        // "Woman over PA", "Young man", "Mrs. Hudson", "Old Woman".
        foreach (var word in words)
        {
            if (RoleWords.TryGetValue(word, out var role))
                return role;
        }

        return !AmbiguousNames.Contains(words[0]) &&
               FirstNames.Value.TryGetValue(words[0].ToLowerInvariant(), out var gender)
            ? gender
            : SpeakerVoiceGender.Unknown;
    }

    /// <summary>
    /// A cue carries a usable label only when one person speaks in it: the label opens the
    /// first line and no other line starts a dialogue turn.
    /// </summary>
    public static bool TryGetSingleSpeakerLabel(string text, out string label)
    {
        label = string.Empty;
        var lines = text.Replace("\r", string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return false;

        var match = LabelRegex().Match(lines[0]);
        if (!match.Success)
            return false;
        if (lines.Skip(1).Any(line => line.TrimStart().StartsWith('-') || LabelRegex().IsMatch(line)))
            return false;

        var name = match.Groups["name"].Value.Trim();
        if (NotSpeakers.Contains(name) || name.Length < 1)
            return false;

        label = name;
        return true;
    }

    private static SpeakerVoiceGender ForcedPitch(IReadOnlyDictionary<int, CueVoiceGenderEvidence> evidence, int cueId) =>
        HardVoiceTurnResolver.TryGetForcedGender(evidence, cueId, out var gender, out _) ? gender : SpeakerVoiceGender.Unknown;

    private static IReadOnlyDictionary<string, SpeakerVoiceGender> LoadFirstNames()
    {
        var result = new Dictionary<string, SpeakerVoiceGender>(StringComparer.Ordinal);
        var assembly = typeof(SpeakerLabelEvidence).Assembly;
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(resource => resource.EndsWith(ResourceName, StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return result;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null)
            return result;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        while (reader.ReadLine() is { } line)
        {
            var tab = line.IndexOf('\t');
            if (tab <= 0 || tab + 1 >= line.Length)
                continue;
            result[line[..tab]] = line[tab + 1] == 'M' ? SpeakerVoiceGender.Male : SpeakerVoiceGender.Female;
        }

        return result;
    }

    // "JACLYN:", "Mrs. Hudson:", "- Man over PA:", "[ Sighs ] Chance:", "NICOLE (on phone):".
    [GeneratedRegex(@"^\s*(?:-\s*)?(?:\[[^\]]*\]\s*|\([^)]*\)\s*)?(?<name>\p{Lu}[\p{L}'.]*(?:[ ][\p{L}'.]+){0,3})\s*(?:\([^)]*\)\s*)?:(?:\s|$)")]
    private static partial Regex LabelRegex();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex WordRegex();
}
