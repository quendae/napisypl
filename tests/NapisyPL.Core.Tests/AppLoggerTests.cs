using NapisyPL.Core.Diagnostics;

namespace NapisyPL.Core.Tests;

public sealed class AppLoggerTests
{
    [Fact]
    public void Logger_RedactsDisallowedFieldsAndKeepsSafeMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-logger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Info(
                "translation_request",
                ("provider", "DeepSeek"),
                ("batch", 3),
                ("apiKey", "super-secret-key"),
                ("authorization", "Bearer secret-token"),
                ("x-api-key", "another-secret"),
                ("prompt", "translate this private subtitle"),
                ("subtitleText", "private dialogue"));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("event=translation_request", log);
            Assert.Contains("provider=DeepSeek", log);
            Assert.Contains("batch=3", log);
            Assert.DoesNotContain("super-secret-key", log);
            Assert.DoesNotContain("secret-token", log);
            Assert.DoesNotContain("another-secret", log);
            Assert.DoesNotContain("translate this private subtitle", log);
            Assert.DoesNotContain("private dialogue", log);
            Assert.Contains("apiKey=[REDACTED]", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Logger_WritesErrorsWithoutLeakingDisallowedValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-logger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Error("translation_failed", ("category", "HttpRequestException"), ("subtitleText", "do not log me"));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("level=ERROR", log);
            Assert.Contains("category=HttpRequestException", log);
            Assert.DoesNotContain("do not log me", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Logger_KeepsPrivacySafeReviewCounters()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-logger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Info(
                "review_window_end",
                ("proposed", 9),
                ("dropOutsideBatch", 1),
                ("dropMissingSpeaker", 2),
                ("dropContextGuard", 3),
                ("dropApplyGuard", 4),
                ("dropApplyCueMismatch", 0),
                ("dropApplyConfidence", 1),
                ("dropApplyFragment", 0),
                ("dropApplyUnchanged", 0),
                ("dropApplyInflection", 2),
                ("dropApplyFindMissing", 1),
                ("dropApplyFindAmbiguous", 0));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("proposed=9", log);
            Assert.Contains("dropOutsideBatch=1", log);
            Assert.Contains("dropMissingSpeaker=2", log);
            Assert.Contains("dropContextGuard=3", log);
            Assert.Contains("dropApplyGuard=4", log);
            Assert.Contains("dropApplyCueMismatch=0", log);
            Assert.Contains("dropApplyConfidence=1", log);
            Assert.Contains("dropApplyFragment=0", log);
            Assert.Contains("dropApplyUnchanged=0", log);
            Assert.Contains("dropApplyInflection=2", log);
            Assert.Contains("dropApplyFindMissing=1", log);
            Assert.Contains("dropApplyFindAmbiguous=0", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Logger_KeepsPrivacySafeLocalRuntimeMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-logger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Info(
                "local_context_runtime",
                ("backend", "vulkan"),
                ("device", "Vulkan1"),
                ("model", "qwen3.5-9b"),
                ("version", "b10809"));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("backend=vulkan", log);
            Assert.Contains("device=Vulkan1", log);
            Assert.Contains("model=qwen3.5-9b", log);
            Assert.DoesNotContain("[REDACTED]", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Logger_KeepsPrivacySafeSpeakerGenderCounters()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-logger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Info(
                "speaker_gender",
                ("speakerCount", 12),
                ("knownGenderCount", 8),
                ("genderEvidenceCount", 3),
                ("topK", 50),
                ("genderSampleCount", 31),
                ("maleTagSampleCount", 18),
                ("femaleTagSampleCount", 11),
                ("anyGenderTagSampleCount", 25),
                ("maleScoreMaxPermille", 812),
                ("femaleScoreMaxPermille", 743),
                ("combinedScoreMeanPermille", 287),
                ("combinedScoreMaxPermille", 901));
            logger.Info(
                "speaker_gender_detail",
                ("speaker", "SPEAKER_07"),
                ("sampleCount", 3),
                ("voiceGender", "unknown"),
                ("reasonCode", "LowCombinedEvidence"),
                ("maleMeanPermille", 41),
                ("femaleMeanPermille", 4),
                ("combinedMeanPermille", 45),
                ("normalizedWinnerPermille", 911),
                ("winnerCount", 0),
                ("oppositeCount", 0),
                ("requiredWinnerCount", 2));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("speakerCount=12", log);
            Assert.Contains("knownGenderCount=8", log);
            Assert.Contains("genderEvidenceCount=3", log);
            Assert.Contains("topK=50", log);
            Assert.Contains("genderSampleCount=31", log);
            Assert.Contains("maleTagSampleCount=18", log);
            Assert.Contains("femaleTagSampleCount=11", log);
            Assert.Contains("anyGenderTagSampleCount=25", log);
            Assert.Contains("maleScoreMaxPermille=812", log);
            Assert.Contains("femaleScoreMaxPermille=743", log);
            Assert.Contains("combinedScoreMeanPermille=287", log);
            Assert.Contains("combinedScoreMaxPermille=901", log);
            Assert.Contains("speaker=SPEAKER_07", log);
            Assert.Contains("reasonCode=LowCombinedEvidence", log);
            Assert.Contains("maleMeanPermille=41", log);
            Assert.Contains("normalizedWinnerPermille=911", log);
            Assert.DoesNotContain("[REDACTED]", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Logger_KeepsPrivacySafeReviewCoverageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-logger-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            logger.Info(
                "review_coverage",
                ("candidateCount", 100),
                ("knownGenderEvidenceCount", 5),
                ("knownSpeakerCandidateCount", 7),
                ("knownAddresseeCandidateCount", 3),
                ("knownSpeakerCandidateIds", "12,45,88"),
                ("knownAddresseeCandidateIds", "45,91"),
                ("subtitleText", "must remain private"));

            var log = File.ReadAllText(logger.LogPath);

            Assert.Contains("knownGenderEvidenceCount=5", log);
            Assert.Contains("knownSpeakerCandidateCount=7", log);
            Assert.Contains("knownAddresseeCandidateCount=3", log);
            Assert.Contains("knownSpeakerCandidateIds=12,45,88", log);
            Assert.Contains("knownAddresseeCandidateIds=45,91", log);
            Assert.DoesNotContain("must remain private", log);
            Assert.DoesNotContain("knownGenderEvidenceCount=[REDACTED]", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
