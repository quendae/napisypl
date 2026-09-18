using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

/// <summary>One line the review could not settle, with the wording it would have had instead.</summary>
public sealed record GenderReviewCandidate(
    int CueId,
    TimeSpan Start,
    TimeSpan End,
    string English,
    string Current,
    string Proposed,
    string Reason);

public sealed partial class DeterministicGenderReviewService
{
    /// <summary>Audio this weak never rewrites a line on its own, but it is enough to ask about one.</summary>
    private const double QuestionableVoiceConfidence = 0.60;

    /// <summary>
    /// Lines worth a person's time: the Polish says the speaker is one gender and what little the
    /// audio has to say points the other way. Everything the review was sure about is already
    /// rewritten, and everything with no evidence at all is not worth asking about, so this is
    /// the middle — Rectify S01 leaves about two dozen of these per season.
    /// </summary>
    public static IReadOnlyList<GenderReviewCandidate> CollectOpenQuestions(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        IReadOnlyList<SubtitleCue> reviewed,
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        IReadOnlyDictionary<int, SpeakerVoiceGender>? labeledCueGender = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(reviewed);

        var sourceById = source.ToDictionary(cue => cue.Index);
        var beforeReview = translated.ToDictionary(cue => cue.Index, cue => cue.Text);
        var questions = new List<GenderReviewCandidate>();
        foreach (var cue in reviewed)
        {
            if (!sourceById.TryGetValue(cue.Index, out var sourceCue))
                continue;

            // The review settled this one on stronger evidence than the leftovers asked about here.
            if (beforeReview.TryGetValue(cue.Index, out var original) &&
                !string.Equals(original, cue.Text, StringComparison.Ordinal))
            {
                continue;
            }

            // Two people share this cue, so one voice cannot answer for the line. The rest of
            // the review skips these, and a question about them would be as blind.
            if (HasMultipleDialogueLines(sourceCue.Text) || HasMultipleDialogueLines(cue.Text))
                continue;

            var (male, female) = CountSelfMarkers(cue.Text);
            var written = male > 0 && female == 0 ? SpeakerVoiceGender.Male
                : female > 0 && male == 0 ? SpeakerVoiceGender.Female
                : SpeakerVoiceGender.Unknown;
            if (written == SpeakerVoiceGender.Unknown)
                continue;

            // The subtitles name this speaker ("Chance:", a few lines up) and the Polish already
            // agrees with the name. A voice reading cannot outvote that, so there is nothing to ask.
            if (labeledCueGender is not null &&
                labeledCueGender.TryGetValue(cue.Index, out var labeled) &&
                labeled == written)
            {
                continue;
            }

            if (!TryGetQuestionableVoice(cueSpeakers, speakerGenderEvidence, cueGenderEvidence, cue.Index, out var heard, out var reason) ||
                heard == written)
            {
                continue;
            }

            var proposed = FixSpeakerAgreement(sourceCue.Text, cue.Text, heard);
            proposed = FixFirstPersonPredicateAgreement(sourceCue.Text, proposed, heard);
            if (string.Equals(proposed, cue.Text, StringComparison.Ordinal))
                continue;

            questions.Add(new GenderReviewCandidate(
                cue.Index, sourceCue.Start, sourceCue.End, sourceCue.Text, cue.Text, proposed, reason));
        }

        return questions;
    }

    /// <summary>The cue's own voice first, then the profile of whoever the diarization put here.</summary>
    private static bool TryGetQuestionableVoice(
        IReadOnlyDictionary<int, string?> cueSpeakers,
        IReadOnlyDictionary<string, SpeakerGenderEvidence> speakerGenderEvidence,
        IReadOnlyDictionary<int, CueVoiceGenderEvidence> cueGenderEvidence,
        int cueId,
        out SpeakerVoiceGender gender,
        out string reason)
    {
        gender = SpeakerVoiceGender.Unknown;
        reason = string.Empty;

        if (cueGenderEvidence.TryGetValue(cueId, out var evidence) &&
            evidence.DirectionalGender != SpeakerVoiceGender.Unknown &&
            evidence.DirectionalConfidence >= QuestionableVoiceConfidence &&
            evidence.DurationSeconds >= MinimumCueDurationSeconds)
        {
            gender = evidence.DirectionalGender;
            reason = $"głos w tej kwestii brzmi {Describe(gender)} ({evidence.DirectionalConfidence:P0})";
            return true;
        }

        if (cueSpeakers.TryGetValue(cueId, out var speaker) &&
            !string.IsNullOrWhiteSpace(speaker) &&
            speakerGenderEvidence.TryGetValue(speaker!, out var profile) &&
            profile.Gender != SpeakerVoiceGender.Unknown &&
            profile.Confidence >= QuestionableVoiceConfidence)
        {
            gender = profile.Gender;
            reason = $"głos tego rozmówcy w całym filmie brzmi {Describe(gender)} ({profile.Confidence:P0})";
            return true;
        }

        return false;
    }

    private static string Describe(SpeakerVoiceGender gender) =>
        gender == SpeakerVoiceGender.Female ? "kobieco" : "męsko";
}

/// <summary>The questions, the subtitles they belong to and the film they can be heard in.</summary>
public sealed record GenderReviewQueueFile(
    string VideoPath,
    string SubtitlePath,
    IReadOnlyList<GenderReviewCandidate> Cues);

/// <summary>The open questions travel next to the subtitles, so they can be answered later.</summary>
public static class GenderReviewQueue
{
    public const string Extension = ".gender-review.json";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string PathFor(string subtitlePath) =>
        System.IO.Path.ChangeExtension(subtitlePath, null) + Extension;

    public static async Task WriteAsync(
        GenderReviewQueueFile queue,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue);
        var path = PathFor(queue.SubtitlePath);
        if (queue.Cues.Count == 0)
        {
            Delete(queue.SubtitlePath);
            return;
        }

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, queue, Options, cancellationToken);
    }

    public static async Task<GenderReviewQueueFile?> ReadAsync(
        string subtitlePath,
        CancellationToken cancellationToken = default)
    {
        var path = PathFor(subtitlePath);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            var queue = await JsonSerializer.DeserializeAsync<GenderReviewQueueFile>(stream, Options, cancellationToken);
            return queue is { Cues.Count: > 0 } ? queue : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Every question answered: the file has nothing left to say.</summary>
    public static void Delete(string subtitlePath)
    {
        var path = PathFor(subtitlePath);
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
