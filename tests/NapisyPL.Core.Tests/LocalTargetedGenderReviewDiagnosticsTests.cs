using System.Net;
using System.Text;
using System.Text.Json;
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
    public async Task ReviewAsync_LogsThirdPersonSubjectConflictSeparately()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"[{\"id\":1,\"find\":\"Zapomniałaś\",\"replace\":\"Zapomniałeś\",\"confidence\":0.99,\"target\":\"addressee\"}"},"finish_reason":"stop"}],"usage":{"prompt_tokens":100,"completion_tokens":20}}
            """);
        using var http = new HttpClient(handler);
        var logger = new RecordingLogger();
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "qwen3.5-9b",
            logger: logger);
        var source = new[]
        {
            Cue(1, "Do you think they're ever gonna forget today?"),
            Cue(2, "Never.")
        };
        var translated = new[]
        {
            Cue(1, "Myślisz, że oni Zapomniałaś o dzisiejszym dniu?"),
            Cue(2, "Nigdy.")
        };
        var speakers = new Dictionary<int, string?>
        {
            [1] = "SPEAKER_A",
            [2] = "SPEAKER_B"
        };
        var evidence = new Dictionary<string, SpeakerGenderEvidence>
        {
            ["SPEAKER_B"] = new(SpeakerVoiceGender.Male, 0.977, 3)
        };

        var result = await service.ReviewAsync(
            source,
            translated,
            speakers,
            speakerGenderEvidence: evidence);

        Assert.Equal(translated[0].Text, result[0].Text);
        var ended = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_window_end"));
        Assert.Equal(1, ended.Int("proposed"));
        Assert.Equal(0, ended.Int("completed"));
        Assert.Equal(1, ended.Int("dropApplyGuard"));
        Assert.Equal(1, ended.Int("dropApplyThirdPersonSubjectConflict"));
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

    [Fact]
    public async Task ReviewAsync_GptOssRequestsLowReasoningWithoutQwenThinkingFlag()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"[]"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":1}}
            """);
        using var http = new HttpClient(handler);
        var service = new LocalTargetedGenderReviewService(
            http,
            "http://127.0.0.1:17843/v1",
            "gpt-oss-20b");
        var source = new[] { Cue(1, "I was ready.") };
        var translated = new[] { Cue(1, "Byłem gotowy.") };

        await service.ReviewAsync(source, translated, new Dictionary<int, string?> { [1] = "SPEAKER_00" });

        using var request = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("low", request.RootElement.GetProperty("reasoning_effort").GetString());
        var templateArgs = request.RootElement.GetProperty("chat_template_kwargs");
        Assert.Equal("low", templateArgs.GetProperty("reasoning_effort").GetString());
        Assert.False(templateArgs.TryGetProperty("enable_thinking", out _));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
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
