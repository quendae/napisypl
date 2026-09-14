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

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);
}
