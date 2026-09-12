using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation.Providers;

namespace NapisyPL.Core.Tests;

public sealed class SubFlow325RegressionTests
{
    [Fact]
    public async Task Argos_TaggedDialogueTurns_AreTranslatedAsSeparateParts()
    {
        var client = new RecordingArgosClient(texts => texts.ToArray());
        var provider = new ArgosOfflineProvider(client);

        await provider.TranslateAsync([
            new TranslationSegment(
                30,
                "<i>- Yeah, sí, problema.</i>\n<i>- And now dos problemas.</i>")
        ]);

        Assert.Equal(
            ["<i>- Yeah, sí, problema.</i>", "<i>- And now dos problemas.</i>"],
            client.LastTexts);
    }

    [Fact]
    public async Task TranslationCache_PreviousSchemaV2Entry_ReturnsMiss()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            const string providerIdentity = "Argos EN→PL|translate-en_pl-1_9.argosmodel|enhanced-cache-v2";
            var source = new[] { Cue(253, "Do you think they're ever gonna\nforget today? Never.") };
            var legacyHash = ComputeLegacyHash(source, providerIdentity, 2);
            var legacyEntry = new
            {
                Version = 2,
                ProviderIdentity = providerIdentity,
                SourceHash = legacyHash,
                Translations = new Dictionary<int, string> { [253] = "STALE V2" }
            };
            await File.WriteAllTextAsync(
                Path.Combine(root, legacyHash + ".json"),
                JsonSerializer.Serialize(legacyEntry));

            var loaded = await new EnhancedTranslationCache(root).TryLoadAsync(source, providerIdentity);

            Assert.Null(loaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Reviewer_ThirdPersonAgreementConflict_UsesNonRedactedDiagnosticKey()
    {
        var handler = new StubHandler("""
            {"choices":[{"message":{"content":"[{\"id\":1,\"find\":\"Zapomniałaś\",\"replace\":\"Zapomniałeś\",\"confidence\":0.99,\"target\":\"addressee\"}]"},"finish_reason":"stop"}],"usage":{"prompt_tokens":100,"completion_tokens":20}}
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

        await service.ReviewAsync(source, translated, speakers, speakerGenderEvidence: evidence);

        var ended = Assert.Single(logger.Entries.Where(entry => entry.EventName == "review_window_end"));
        Assert.Equal(1, ended.Int("dropApplyGuard"));
        Assert.Equal(1, ended.Int("dropApplyThirdPersonAgreementConflict"));
    }

    private static SubtitleCue Cue(int id, string text) =>
        new(id, TimeSpan.FromSeconds(id), TimeSpan.FromSeconds(id + 1), text);

    private static string ComputeLegacyHash(
        IReadOnlyList<SubtitleCue> source,
        string providerIdentity,
        int version)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, $"subflow-enhanced-translation-cache-v{version}\n");
        Append(hash, providerIdentity);
        Append(hash, "\n");
        foreach (var cue in source)
        {
            Append(hash, cue.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, ":");
            Append(hash, cue.Text.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(hash, ":");
            Append(hash, cue.Text);
            Append(hash, "\n");
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static void Append(IncrementalHash hash, string value) =>
        hash.AppendData(Encoding.UTF8.GetBytes(value));

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "SubFlow-325-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }

    private sealed class RecordingArgosClient(Func<IReadOnlyList<string>, IReadOnlyList<string>> translate) : IArgosTranslatorClient
    {
        public IReadOnlyList<string> LastTexts { get; private set; } = [];

        public Task<IReadOnlyList<string>> TranslateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            LastTexts = texts.ToArray();
            return Task.FromResult(translate(texts));
        }
    }

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
    }
}
