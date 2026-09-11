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
}
