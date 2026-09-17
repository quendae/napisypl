using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class HardVoiceDirectionalFallbackTests
{
    [Fact]
    public void Resolve_LowCombinedDirectionalEvidence_DoesNotResolve()
    {
        var cues = new[]
        {
            Cue(1, 0, 2, "Did you do it?"),
            Cue(2, 2, 4, "Yes.")
        };
        var evidence = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = DirectionalOnly(SpeakerVoiceGender.Male, 0.990, combined: 0.020),
            [2] = DirectionalOnly(SpeakerVoiceGender.Female, 0.975, combined: 0.010)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            evidence,
            1);

        Assert.False(result.IsResolved);
        Assert.Equal("current_gender_unknown", result.ReasonCode);
    }

    [Fact]
    public void Review_LowCombinedDirectionalMale_WithStableSpeaker_FixesChuckStyleSelfForms()
    {
        var source = new[]
        {
            Cue(451, 0, 2, "I didn't get sick because I read about you in the paper."),
            Cue(452, 2, 4, "I got sick because I went outside to get the paper.")
        };
        var translated = new[]
        {
            Cue(451, 0, 2, "Nie zachorowałam, bo czytałam o tobie w gazecie."),
            Cue(452, 2, 4, "Zachorowałam, bo wyszłam z domu po gazetę.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [451] = "SPEAKER_96",
            [452] = "SPEAKER_96"
        };
        var speakerEvidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_96"] = new(SpeakerVoiceGender.Male, 0.985, 3)
        };
        var evidence = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [451] = DirectionalOnly(SpeakerVoiceGender.Male, 0.990, combined: 0.020),
            [452] = DirectionalOnly(SpeakerVoiceGender.Male, 1.000, combined: 0.009)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerEvidence,
            evidence,
            hardVoiceTurnOnly: true);

        Assert.Equal("Nie zachorowałem, bo czytałem o tobie w gazecie.", result[0].Text);
        Assert.Equal("Zachorowałem, bo wyszedłem z domu po gazetę.", result[1].Text);
    }

    [Fact]
    public void Resolve_NoDirectionalSignal_RemainsUnknown()
    {
        var cues = new[]
        {
            Cue(1, 0, 2, "One."),
            Cue(2, 2, 4, "Two.")
        };
        var evidence = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [1] = new(SpeakerVoiceGender.Unknown, 0, 0, 2),
            [2] = DirectionalOnly(SpeakerVoiceGender.Female, 0.99, combined: 0.01)
        };

        var result = HardVoiceTurnResolver.Resolve(
            cues,
            new Dictionary<int, string?>(),
            evidence,
            1);

        Assert.False(result.IsResolved);
        Assert.Equal("current_gender_unknown", result.ReasonCode);
    }

    private static CueVoiceGenderEvidence DirectionalOnly(
        SpeakerVoiceGender gender,
        double confidence,
        double combined) =>
        new(
            SpeakerVoiceGender.Unknown,
            0,
            combined,
            2,
            gender,
            confidence);

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
