using System.Net;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTargetedGenderReviewTimeoutTests
{
    [Fact]
    public async Task ReviewAsync_WhenWindowTimesOut_PreservesBaseTranslationAndLogsTimeout()
    {
        using var http = new HttpClient(new DelayedHandler()) { Timeout = Timeout.InfiniteTimeSpan };
        var logger = new RecordingLogger();
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3-1.7b",
            logger: logger,
            requestTimeout: TimeSpan.FromMilliseconds(50));
        var source = new[] { Cue(1, "I was ready.") };
        var translated = new[] { Cue(1, "Byłem gotowy.") };
        var statuses = new List<string>();

        var result = await service.ReviewAsync(
            source,
            translated,
            new Dictionary<int, string?> { [1] = "SPEAKER_00" },
            status: new InlineProgress<string>(statuses.Add));

        Assert.Equal("Byłem gotowy.", result[0].Text);
        var error = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_window_error"));
        Assert.Equal("review_timeout", error.Fields["reasonCode"]?.ToString());
        Assert.Contains(statuses, value => value.Contains("limit czasu", StringComparison.OrdinalIgnoreCase));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class DelayedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<Entry> Entries { get; } = [];
        public void Info(string eventName, params (string Key, object? Value)[] fields) => Add(eventName, fields);
        public void Error(string eventName, params (string Key, object? Value)[] fields) => Add(eventName, fields);
        private void Add(string eventName, IReadOnlyList<(string Key, object? Value)> fields) =>
            Entries.Add(new Entry(eventName, fields.ToDictionary(field => field.Key, field => field.Value)));
    }

    private sealed record Entry(string EventName, IReadOnlyDictionary<string, object?> Fields);
}
