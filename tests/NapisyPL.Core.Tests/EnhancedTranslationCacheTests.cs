using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class EnhancedTranslationCacheTests
{
    [Fact]
    public async Task SaveThenLoad_SameSourceAndProvider_ReturnsCachedTranslationWithCurrentTiming()
    {
        var root = TempDirectory();
        try
        {
            var cache = new EnhancedTranslationCache(root);
            var source = new[]
            {
                Cue(1, 1, 2, "Hello."),
                Cue(2, 3, 4, "How are you?")
            };
            var translated = new[]
            {
                source[0] with { Text = "Cześć." },
                source[1] with { Text = "Jak się masz?" }
            };

            await cache.SaveAsync(source, translated, "argos-en-pl-1_9");

            var sameTextWithNewTiming = new[]
            {
                Cue(1, 10, 11, "Hello."),
                Cue(2, 12, 13, "How are you?")
            };
            var loaded = await new EnhancedTranslationCache(root)
                .TryLoadAsync(sameTextWithNewTiming, "argos-en-pl-1_9");

            Assert.NotNull(loaded);
            Assert.Equal("Cześć.", loaded![0].Text);
            Assert.Equal("Jak się masz?", loaded[1].Text);
            Assert.Equal(TimeSpan.FromSeconds(10), loaded[0].Start);
            Assert.Equal(TimeSpan.FromSeconds(13), loaded[1].End);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TryLoad_WhenSourceTextChanges_ReturnsMiss()
    {
        var root = TempDirectory();
        try
        {
            var cache = new EnhancedTranslationCache(root);
            var source = new[] { Cue(1, 1, 2, "Hello.") };
            await cache.SaveAsync(source, new[] { source[0] with { Text = "Cześć." } }, "argos-en-pl-1_9");

            var changed = new[] { Cue(1, 1, 2, "Hello there.") };
            var loaded = await cache.TryLoadAsync(changed, "argos-en-pl-1_9");

            Assert.Null(loaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TryLoad_WhenProviderIdentityChanges_ReturnsMiss()
    {
        var root = TempDirectory();
        try
        {
            var cache = new EnhancedTranslationCache(root);
            var source = new[] { Cue(1, 1, 2, "Hello.") };
            await cache.SaveAsync(source, new[] { source[0] with { Text = "Cześć." } }, "argos-en-pl-1_9");

            var loaded = await cache.TryLoadAsync(source, "argos-en-pl-2_0");

            Assert.Null(loaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task TryLoad_LegacySchemaV1Entry_ReturnsMiss()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            const string providerIdentity = "Argos EN→PL|translate-en_pl-1_9.argosmodel|enhanced-cache-v2";
            var source = new[] { Cue(253, 1, 2, "Do you think they're ever gonna\nforget today? Never.") };
            var legacyHash = ComputeLegacyV1Hash(source, providerIdentity);
            var legacyEntry = new
            {
                Version = 1,
                ProviderIdentity = providerIdentity,
                SourceHash = legacyHash,
                Translations = new Dictionary<int, string> { [253] = "STALE" }
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
    public async Task TryLoad_WhenEntryIsCorrupt_ReturnsMissInsteadOfThrowing()
    {
        var root = TempDirectory();
        try
        {
            var cache = new EnhancedTranslationCache(root);
            var source = new[] { Cue(1, 1, 2, "Hello.") };
            await cache.SaveAsync(source, new[] { source[0] with { Text = "Cześć." } }, "argos-en-pl-1_9");
            var cacheFile = Assert.Single(Directory.GetFiles(root, "*.json"));
            await File.WriteAllTextAsync(cacheFile, "broken");

            var loaded = await cache.TryLoadAsync(source, "argos-en-pl-1_9");

            Assert.Null(loaded);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string ComputeLegacyV1Hash(IReadOnlyList<SubtitleCue> source, string providerIdentity)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "subflow-enhanced-translation-cache-v1\n");
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

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "SubFlow-translation-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
