using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NapisyPL.Core.ContextResolution;

public sealed class SpeakerDiarizationCache(string cacheDirectory, string modelSignature)
{
    private readonly string _cacheDirectory = cacheDirectory;
    private readonly string _modelSignature = string.IsNullOrWhiteSpace(modelSignature)
        ? throw new ArgumentException("Model signature is required.", nameof(modelSignature))
        : modelSignature;

    public static SpeakerDiarizationCache CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new SpeakerDiarizationCache(
            Path.Combine(localAppData, "SubFlow", "diarization-cache"),
            "sherpa-pyannote3-campplus-v1");
    }

    public async Task<IReadOnlyList<SpeakerSegment>?> TryLoadAsync(
        string mediaPath,
        CancellationToken cancellationToken = default)
    {
        var info = new FileInfo(mediaPath);
        if (!info.Exists)
            return null;

        var cachePath = GetCachePath(mediaPath);
        if (!File.Exists(cachePath))
            return null;

        try
        {
            await using var stream = File.OpenRead(cachePath);
            var payload = await JsonSerializer.DeserializeAsync<CachePayload>(stream, cancellationToken: cancellationToken);
            if (payload is null ||
                !string.Equals(payload.ModelSignature, _modelSignature, StringComparison.Ordinal) ||
                payload.MediaLength != info.Length ||
                payload.MediaLastWriteUtcTicks != info.LastWriteTimeUtc.Ticks ||
                payload.Segments is null || payload.Segments.Count == 0)
            {
                return null;
            }

            return payload.Segments
                .Where(segment => segment.EndSeconds > segment.StartSeconds && segment.Speaker >= 0)
                .Select(segment => new SpeakerSegment(segment.StartSeconds, segment.EndSeconds, segment.Speaker))
                .ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public async Task SaveAsync(
        string mediaPath,
        IReadOnlyList<SpeakerSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return;

        var info = new FileInfo(mediaPath);
        if (!info.Exists)
            return;

        Directory.CreateDirectory(_cacheDirectory);
        var cachePath = GetCachePath(mediaPath);
        var partialPath = cachePath + ".partial";
        var payload = new CachePayload(
            _modelSignature,
            info.Length,
            info.LastWriteTimeUtc.Ticks,
            segments.Select(segment => new CachedSegment(segment.StartSeconds, segment.EndSeconds, segment.Speaker)).ToArray());

        try
        {
            await using (var stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await JsonSerializer.SerializeAsync(stream, payload, cancellationToken: cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(partialPath, cachePath, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(partialPath)) File.Delete(partialPath); } catch { }
        }
    }

    private string GetCachePath(string mediaPath)
    {
        var normalized = Path.GetFullPath(mediaPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
        return Path.Combine(_cacheDirectory, hash + ".json");
    }

    private sealed record CachePayload(
        string ModelSignature,
        long MediaLength,
        long MediaLastWriteUtcTicks,
        IReadOnlyList<CachedSegment> Segments);

    private sealed record CachedSegment(double StartSeconds, double EndSeconds, int Speaker);
}
