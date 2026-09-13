using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class LoggingTranslationProviderTests
{
    [Fact]
    public async Task TranslateAsync_LogsMetadataAndTimingWithoutSubtitleText()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-provider-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            var provider = new LoggingTranslationProvider(new FakeProvider(), logger);
            var segments = new[]
            {
                new TranslationSegment(1, "SECRET SOURCE ONE"),
                new TranslationSegment(2, "SECRET SOURCE TWO")
            };

            var result = await provider.TranslateAsync(segments);
            var log = File.ReadAllText(logger.LogPath);

            Assert.Equal("SECRET TRANSLATION 1", result[1]);
            Assert.Contains("event=translation_request_start", log);
            Assert.Contains("event=translation_request_end", log);
            Assert.Contains("provider=fake-provider", log);
            Assert.Contains("segmentCount=2", log);
            Assert.Contains($"characterCount={segments.Sum(x => x.Text.Length)}", log);
            Assert.Contains("elapsedMs=", log);
            Assert.Contains("result=success", log);
            Assert.DoesNotContain("SECRET SOURCE", log);
            Assert.DoesNotContain("SECRET TRANSLATION", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task TranslateAsync_LogsErrorCategoryAndRethrows()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-provider-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var logger = new AppLogger(root);
            var provider = new LoggingTranslationProvider(new ThrowingProvider(), logger);

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                provider.TranslateAsync([new TranslationSegment(1, "PRIVATE") ]));

            var log = File.ReadAllText(logger.LogPath);
            Assert.Contains("event=translation_request_error", log);
            Assert.Contains("category=HttpRequestException", log);
            Assert.Contains("result=error", log);
            Assert.DoesNotContain("PRIVATE", log);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        public string DisplayName => "fake-provider";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default)
        {
            IReadOnlyDictionary<int, string> result = segments.ToDictionary(x => x.Id, x => $"SECRET TRANSLATION {x.Id}");
            return Task.FromResult(result);
        }
    }

    private sealed class ThrowingProvider : ITranslationProvider
    {
        public string DisplayName => "throwing-provider";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default) =>
            throw new HttpRequestException("simulated failure");
    }
}
