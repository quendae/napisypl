using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed class EnhancedTranslationCache(string cacheDirectory)
{
    private const int CacheVersion = 1;
    private readonly string _cacheDirectory = cacheDirectory;

    public static EnhancedTranslationCache CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new EnhancedTranslationCache(Path.Combine(localAppData, "SubFlow", "cache", "translations"));
    }

    public async Task<IReadOnlyList<SubtitleCue>?> TryLoadAsync(
        IReadOnlyList<SubtitleCue> source,
        string providerIdentity,
        CancellationToken cancellationToken = default)
    {
        var sourceHash = ComputeSourceHash(source, providerIdentity);
        var path = GetCachePath(sourceHash);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
            var entry = JsonSerializer.Deserialize<CacheEntry>(json);
            if (entry is null ||
                entry.Version != CacheVersion ||
                !string.Equals(entry.ProviderIdentity, providerIdentity, StringComparison.Ordinal) ||
                !string.Equals(entry.SourceHash, sourceHash, StringComparison.Ordinal) ||
                entry.Translations.Count != source.Count)
                return null;

            var output = new SubtitleCue[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                var cue = source[i];
                if (!entry.Translations.TryGetValue(cue.Index, out var text))
                    return null;
                output[i] = cue with { Text = text };
            }

            return output;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        IReadOnlyList<SubtitleCue> source,
        IReadOnlyList<SubtitleCue> translated,
        string providerIdentity,
        CancellationToken cancellationToken = default)
    {
        if (source.Count != translated.Count)
            throw new ArgumentException("Source and translated cue counts must match.", nameof(translated));

        var translations = new Dictionary<int, string>(source.Count);
        for (var i = 0; i < source.Count; i++)
        {
            if (source[i].Index != translated[i].Index)
                throw new ArgumentException("Source and translated cue ids must match.", nameof(translated));
            translations.Add(source[i].Index, translated[i].Text);
        }

        var sourceHash = ComputeSourceHash(source, providerIdentity);
        var entry = new CacheEntry(CacheVersion, providerIdentity, sourceHash, translations);
        var json = JsonSerializer.Serialize(entry);

        Directory.CreateDirectory(_cacheDirectory);
        var path = GetCachePath(sourceHash);
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(temporaryPath, json, new UTF8Encoding(false), cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporaryPath); } catch { }
        }
    }

    private string GetCachePath(string sourceHash) =>
        Path.Combine(_cacheDirectory, sourceHash + ".json");

    private static string ComputeSourceHash(
        IReadOnlyList<SubtitleCue> source,
        string providerIdentity)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, $"subflow-enhanced-translation-cache-v{CacheVersion}\n");
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

    private sealed record CacheEntry(
        int Version,
        string ProviderIdentity,
        string SourceHash,
        Dictionary<int, string> Translations);
}
