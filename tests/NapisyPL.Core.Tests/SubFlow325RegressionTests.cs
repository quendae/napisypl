using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class SubFlow325RegressionTests
{
    [Fact]
    public async Task TranslationCache_PreviousSchemaV2Entry_ReturnsMiss()
    {
        var root = TempDirectory();
        try
        {
            Directory.CreateDirectory(root);
            const string providerIdentity = "MADLAD-400 3B — Quality|enhanced-cache-v2";
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
}
