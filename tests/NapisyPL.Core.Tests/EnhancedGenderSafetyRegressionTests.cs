using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class EnhancedGenderSafetyRegressionTests
{
    [Fact]
    public void Review_WhenAddresseeSpeakerGenderConflictsWithNextCueAudio_DoesNotRewrite()
    {
        var source = new[]
        {
            Cue(1, 0.0, 1.0, "What did you do?"),
            Cue(2, 1.2, 2.2, "Nothing much.")
        };
        var translated = new[]
        {
            Cue(1, 0.0, 1.0, "Co zrobiłaś?"),
            Cue(2, 1.2, 2.2, "Nic wielkiego.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_00",
            [2] = "SPEAKER_02"
        };
        var speakerEvidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_02"] = new(SpeakerVoiceGender.Male, 0.94, 3)
        };
        var cueEvidence = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [2] = new(SpeakerVoiceGender.Female, 0.95, 0.12, 1.0)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerEvidence,
            cueEvidence);

        Assert.Equal("Co zrobiłaś?", result[0].Text);
    }

    [Fact]
    public void Review_HardVoiceWithoutStableSpeaker_PreservesStrongConsistentSelfGenderInsideCue()
    {
        var source = new[]
        {
            Cue(184, 0.0, 3.0, "If I was 40 years younger, I'd make you buy me a piña colada.")
        };
        var translated = new[]
        {
            Cue(184, 0.0, 3.0, "Gdybym był 40 lat młodszy, kazałbym ci kupić mi piña colada.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [184] = "SPEAKER_01"
        };
        var cueEvidence = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [184] = new(SpeakerVoiceGender.Female, 0.95, 0.12, 3.0)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            new Dictionary<string, SpeakerGenderEvidence>(),
            cueEvidence,
            hardVoiceTurnOnly: true);

        Assert.Equal("Gdybym był 40 lat młodszy, kazałbym ci kupić mi piña colada.", result[0].Text);
    }

    [Fact]
    public void Review_HardVoice_PreservesStrongConsistentAddresseeGenderInsideCue()
    {
        var source = new[]
        {
            Cue(551, 0.0, 3.0, "God, if you knew how crazy I am about you, you wouldn't hesitate."),
            Cue(552, 3.1, 5.0, "I can't sleep.")
        };
        var translated = new[]
        {
            Cue(551, 0.0, 3.0, "Boże, gdybyś wiedział, jak bardzo na ciebie szaleję, nie zawahałbyś się."),
            Cue(552, 3.1, 5.0, "Nie mogę spać.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [551] = "SPEAKER_A",
            [552] = "SPEAKER_B"
        };
        var cueEvidence = new Dictionary<int, CueVoiceGenderEvidence>
        {
            [551] = DirectionalOnly(SpeakerVoiceGender.Male, 0.99, 0.02, 3.0),
            [552] = DirectionalOnly(SpeakerVoiceGender.Female, 0.90, 0.07, 1.9)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            new Dictionary<string, SpeakerGenderEvidence>(),
            cueEvidence,
            hardVoiceTurnOnly: true);

        Assert.Equal("Boże, gdybyś wiedział, jak bardzo na ciebie szaleję, nie zawahałbyś się.", result[0].Text);
    }

    [Fact]
    public void Review_HardVoiceStableFemaleSpeaker_NormalizesFirstPersonPredicateAndVerbTogether()
    {
        var source = new[]
        {
            Cue(549, 0.0, 3.0, "I'm not shy, but I've never kissed you before.")
        };
        var translated = new[]
        {
            Cue(549, 0.0, 3.0, "Nie jestem nieśmiały, ale nigdy cię wcześniej nie pocałowałem.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [549] = "SPEAKER_07"
        };
        var speakerEvidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_07"] = new(SpeakerVoiceGender.Female, 0.938, 3)
        };

        var result = new DeterministicGenderReviewService().Review(
            source,
            translated,
            speakers,
            speakerEvidence,
            hardVoiceTurnOnly: true);

        Assert.Equal("Nie jestem nieśmiała, ale nigdy cię wcześniej nie pocałowałam.", result[0].Text);
    }

    [Fact]
    public void Logger_KeepsEnhancedNextCueDiagnosticsVisible()
    {
        var root = Path.Combine(Path.GetTempPath(), "SubFlow-enhanced-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Info(
                "enhanced_gender_cue",
                ("uniqueSpeakerCount", 23),
                ("nextCue", 262),
                ("nextSpeaker", "SPEAKER_02"),
                ("nextCueGender", "female"),
                ("nextCueConfidencePermille", 951),
                ("nextCueCombinedPermille", 120),
                ("nextCueDurationMs", 2545),
                ("nextSpeakerGender", "male"),
                ("nextSpeakerConfidencePermille", 939),
                ("nextSpeakerSampleCount", 3));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("uniqueSpeakerCount=23", log);
            Assert.Contains("nextCue=262", log);
            Assert.Contains("nextSpeaker=SPEAKER_02", log);
            Assert.Contains("nextCueGender=female", log);
            Assert.Contains("nextCueConfidencePermille=951", log);
            Assert.Contains("nextCueCombinedPermille=120", log);
            Assert.Contains("nextCueDurationMs=2545", log);
            Assert.Contains("nextSpeakerGender=male", log);
            Assert.Contains("nextSpeakerConfidencePermille=939", log);
            Assert.Contains("nextSpeakerSampleCount=3", log);
            Assert.DoesNotContain("[REDACTED]", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static CueVoiceGenderEvidence DirectionalOnly(
        SpeakerVoiceGender gender,
        double confidence,
        double combined,
        double durationSeconds) =>
        new(
            SpeakerVoiceGender.Unknown,
            0,
            combined,
            durationSeconds,
            gender,
            confidence);

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
