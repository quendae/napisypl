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
}
