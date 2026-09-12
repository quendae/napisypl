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

    private static SubtitleCue Cue(int id, double start, double end, string text) =>
        new(id, TimeSpan.FromSeconds(start), TimeSpan.FromSeconds(end), text);

    private static string TempDirectory() =>
        Path.Combine(Path.GetTempPath(), "SubFlow-translation-cache-tests-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
