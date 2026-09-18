using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

/// <summary>
/// The lines handed to a person: audio too weak to rewrite a line on its own, but pointing the
/// other way than the Polish does.
/// </summary>
public sealed class GenderReviewQueueTests
{
    private static SubtitleCue Cue(int index, string text) =>
        new(index, TimeSpan.FromSeconds(index * 2), TimeSpan.FromSeconds(index * 2 + 1.5), text);

    private static IReadOnlyList<GenderReviewCandidate> Ask(
        string polish,
        string reviewedPolish,
        SpeakerGenderEvidence? profile = null,
        CueVoiceGenderEvidence? cueVoice = null)
    {
        var source = new[] { Cue(1, "I told them everything.") };
        var cueGender = new Dictionary<int, CueVoiceGenderEvidence>();
        if (cueVoice is not null)
            cueGender[1] = cueVoice;

        return DeterministicGenderReviewService.CollectOpenQuestions(
            source,
            [Cue(1, polish)],
            [Cue(1, reviewedPolish)],
            new Dictionary<int, string?> { [1] = "SPEAKER_01" },
            profile is null
                ? new Dictionary<string, SpeakerGenderEvidence>()
                : new Dictionary<string, SpeakerGenderEvidence> { ["SPEAKER_01"] = profile },
            cueGender);
    }

    [Fact]
    public void AVoiceTooWeakToActOnIsStillWorthAsking()
    {
        var questions = Ask(
            "Powiedziałem im wszystko.",
            "Powiedziałem im wszystko.",
            profile: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.70, 4));

        var question = Assert.Single(questions);
        Assert.Equal("Powiedziałam im wszystko.", question.Proposed);
        Assert.Contains("kobieco", question.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void AVoiceThatAgreesWithTheTextAsksNothing() =>
        Assert.Empty(Ask(
            "Powiedziałem im wszystko.",
            "Powiedziałem im wszystko.",
            profile: new SpeakerGenderEvidence(SpeakerVoiceGender.Male, 0.70, 4)));

    [Fact]
    public void AVoiceTooFaintToMeanAnythingAsksNothing() =>
        Assert.Empty(Ask(
            "Powiedziałem im wszystko.",
            "Powiedziałem im wszystko.",
            profile: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.40, 4)));

    [Fact]
    public void ALineTheReviewAlreadySettledIsNotReopened() =>
        // The review changed this one on stronger evidence; the leftovers do not get to argue.
        Assert.Empty(Ask(
            "Powiedziałem im wszystko.",
            "Powiedziałam im wszystko.",
            profile: new SpeakerGenderEvidence(SpeakerVoiceGender.Male, 0.70, 4)));

    [Fact]
    public void ALineWithNoGenderInItAsksNothing() =>
        Assert.Empty(Ask(
            "Mówię im wszystko.",
            "Mówię im wszystko.",
            profile: new SpeakerGenderEvidence(SpeakerVoiceGender.Female, 0.95, 4)));

    [Fact]
    public void ALineTheSubtitlesNameTheSpeakerOfIsNotAsked() =>
        // "Chance:" stands two lines up and the Polish agrees with it; a voice reading of the
        // scene cannot outvote a name written into the subtitles (Chance S01E10 #312).
        Assert.Empty(DeterministicGenderReviewService.CollectOpenQuestions(
            [new SubtitleCue(1, TimeSpan.Zero, TimeSpan.FromSeconds(2), "I was scared, D.")],
            [Cue(1, "Bałem się, D.")],
            [Cue(1, "Bałem się, D.")],
            new Dictionary<int, string?> { [1] = "SPEAKER_01" },
            new Dictionary<string, SpeakerGenderEvidence>
                { ["SPEAKER_01"] = new(SpeakerVoiceGender.Female, 0.85, 5) },
            new Dictionary<int, CueVoiceGenderEvidence>(),
            new Dictionary<int, SpeakerVoiceGender> { [1] = SpeakerVoiceGender.Male }));

    [Fact]
    public async Task TheQuestionsSurviveARoundTripAndVanishWhenAnswered()
    {
        var directory = Directory.CreateTempSubdirectory("subflow-review");
        try
        {
            var subtitlePath = Path.Combine(directory.FullName, "film.pl.srt");
            var queue = new GenderReviewQueueFile(
                Path.Combine(directory.FullName, "film.mkv"),
                subtitlePath,
                [new GenderReviewCandidate(7, TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(14),
                    "I told them.", "Powiedziałem im.", "Powiedziałam im.", "głos brzmi kobieco (70%)")]);

            await GenderReviewQueue.WriteAsync(queue);
            var read = await GenderReviewQueue.ReadAsync(subtitlePath);

            Assert.NotNull(read);
            Assert.Equal(queue.VideoPath, read!.VideoPath);
            var candidate = Assert.Single(read.Cues);
            Assert.Equal(7, candidate.CueId);
            Assert.Equal("Powiedziałam im.", candidate.Proposed);
            Assert.Equal(TimeSpan.FromSeconds(12), candidate.Start);

            await GenderReviewQueue.WriteAsync(queue with { Cues = [] });
            Assert.Null(await GenderReviewQueue.ReadAsync(subtitlePath));
            Assert.False(File.Exists(GenderReviewQueue.PathFor(subtitlePath)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
