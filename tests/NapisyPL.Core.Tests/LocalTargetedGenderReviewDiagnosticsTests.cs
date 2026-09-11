using System.Net;
using System.Text;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTargetedGenderReviewDiagnosticsTests
{
    [Fact]
    public async Task ReviewAsync_LogsPerWindowTimingAndTokenMetadataWithoutSubtitleText()
    {
        const string sourceText = "PRIVATE SOURCE DIALOGUE";
        const string translatedText = "Byłem gotowy. PRIVATE POLISH DIALOGUE";
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"[]"},"finish_reason":"stop"}],"usage":{"prompt_tokens":321,"completion_tokens":7,"total_tokens":328}}
            """);
        using var http = new HttpClient(handler);
        var logger = new RecordingLogger();
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3-1.7b",
            logger: logger);
        var source = new[] { Cue(1, sourceText) };
        var translated = new[] { Cue(1, translatedText) };

        await service.ReviewAsync(source, translated, new Dictionary<int, string?> { [1] = "SPEAKER_00" });

        var plan = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_plan"));
        Assert.Equal(1, plan.Int("candidateCount"));
        Assert.Equal(1, plan.Int("windowCount"));

        var started = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_window_start"));
        Assert.Equal(1, started.Int("windowIndex"));
        Assert.Equal(1, started.Int("windowCount"));
        Assert.Equal(1, started.Int("cueCount"));
        Assert.Equal(1, started.Int("candidateCount"));
        Assert.True(started.Int("promptChars") > 0);
        Assert.Equal(1, started.Int("speakerCount"));

        var ended = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_window_end"));
        Assert.Equal(321, ended.Int("promptTokens"));
        Assert.Equal(7, ended.Int("completionTokens"));
        Assert.Equal("stop", ended.String("finishReason"));
        Assert.Equal("success", ended.String("result"));
        Assert.True(ended.Double("elapsedMs") >= 0);
        Assert.True(ended.Int("responseChars") > 0);

        var allLoggedValues = string.Join(" ", logger.Entries.SelectMany(entry => entry.Fields.Values).Select(value => value?.ToString()));
        Assert.DoesNotContain(sourceText, allLoggedValues, StringComparison.Ordinal);
        Assert.DoesNotContain(translatedText, allLoggedValues, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReviewAsync_DefaultsToAtMostFiveCandidatesPerWindowForLocalSmallModel()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"[]"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":1}}
            """);
        using var http = new HttpClient(handler);
        var logger = new RecordingLogger();
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3-1.7b",
            logger: logger);
        var source = Enumerable.Range(1, 6).Select(id => Cue(id, $"Source {id}")).ToArray();
        var translated = Enumerable.Range(1, 6).Select(id => Cue(id, "Byłem gotowy.")).ToArray();
        var speakers = Enumerable.Range(1, 6).ToDictionary(id => id, id => (string?)$"SPEAKER_{id % 2:00}");

        await service.ReviewAsync(source, translated, speakers);

        var plan = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_plan"));
        Assert.Equal(6, plan.Int("candidateCount"));
        Assert.Equal(2, plan.Int("windowCount"));
        Assert.Equal(2, logger.Entries.Count(entry => entry.EventName == "review_window_start"));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            });
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<Entry> Entries { get; } = [];

        public void Info(string eventName, params (string Key, object? Value)[] fields) => Add(eventName, fields);
        public void Error(string eventName, params (string Key, object? Value)[] fields) => Add(eventName, fields);

        private void Add(string eventName, IReadOnlyList<(string Key, object? Value)> fields) =>
            Entries.Add(new Entry(eventName, fields.ToDictionary(field => field.Key, field => field.Value, StringComparer.OrdinalIgnoreCase)));
    }

    private sealed record Entry(string EventName, IReadOnlyDictionary<string, object?> Fields)
    {
        public int Int(string key) => Convert.ToInt32(Fields[key], System.Globalization.CultureInfo.InvariantCulture);
        public double Double(string key) => Convert.ToDouble(Fields[key], System.Globalization.CultureInfo.InvariantCulture);
        public string String(string key) => Convert.ToString(Fields[key], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
