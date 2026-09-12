using System.Text.Json;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public static class FirefoxBergamotModelResolver
{
    private const string AttachmentBaseUrl = "https://firefox-settings-attachments.cdn.mozilla.net/";

    public static BergamotModelDescriptor Resolve(string registryJson, string sourceLanguage, string targetLanguage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registryJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLanguage);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);

        using var document = JsonDocument.Parse(registryJson);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Firefox translations registry does not contain a data array.");
        }

        var records = data.EnumerateArray()
            .Select(ParseRecord)
            .Where(record =>
                string.Equals(record.SourceLanguage, sourceLanguage, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(record.TargetLanguage, targetLanguage, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(record.FilterExpression))
            .ToArray();

        var groups = records
            .GroupBy(record => (record.Architecture, record.Version))
            .OrderBy(group => ArchitecturePriority(group.Key.Architecture))
            .ThenByDescending(group => group.Key.Version, VersionStringComparer.Instance);

        foreach (var group in groups)
        {
            var model = group.FirstOrDefault(record => record.FileType == "model");
            var shortlist = group.FirstOrDefault(record => record.FileType == "lex");
            var sharedVocab = group.FirstOrDefault(record => record.FileType == "vocab");
            var sourceVocab = sharedVocab ?? group.FirstOrDefault(record => record.FileType == "srcvocab");
            var targetVocab = sharedVocab ?? group.FirstOrDefault(record => record.FileType == "trgvocab");

            if (model is null || shortlist is null || sourceVocab is null || targetVocab is null)
            {
                continue;
            }

            var modelAsset = ToAsset(model);
            var shortlistAsset = ToAsset(shortlist);
            var sourceVocabAsset = ToAsset(sourceVocab);
            var targetVocabAsset = ReferenceEquals(sourceVocab, targetVocab)
                ? sourceVocabAsset
                : ToAsset(targetVocab);

            return new BergamotModelDescriptor
            {
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                ModelVersion = group.Key.Version,
                Model = modelAsset,
                SourceVocab = sourceVocabAsset,
                TargetVocab = targetVocabAsset,
                Shortlist = shortlistAsset,
            };
        }

        throw new InvalidDataException($"No complete desktop Bergamot model was found for {sourceLanguage}->{targetLanguage}.");
    }

    private static RegistryRecord ParseRecord(JsonElement element)
    {
        var attachment = element.GetProperty("attachment");
        var sourceLanguage = GetString(element, "sourceLanguage", "fromLang");
        var targetLanguage = GetString(element, "targetLanguage", "toLang");
        var downloadHash = attachment.GetProperty("hash").GetString() ?? string.Empty;
        var downloadSize = attachment.GetProperty("size").GetInt64();
        var downloadFileName = attachment.GetProperty("filename").GetString() ?? string.Empty;
        var installedHash = element.TryGetProperty("decompressedHash", out var decompressedHash)
            ? decompressedHash.GetString() ?? string.Empty
            : downloadHash;
        var installedSize = element.TryGetProperty("decompressedSize", out var decompressedSize)
            ? decompressedSize.GetInt64()
            : downloadSize;
        var installedFileName = element.TryGetProperty("name", out var name) && !string.IsNullOrWhiteSpace(name.GetString())
            ? name.GetString()!
            : downloadFileName;

        return new RegistryRecord(
            SourceLanguage: sourceLanguage,
            TargetLanguage: targetLanguage,
            Architecture: element.TryGetProperty("architecture", out var architecture) ? architecture.GetString() ?? string.Empty : string.Empty,
            Version: element.GetProperty("version").GetString() ?? string.Empty,
            FileType: element.GetProperty("fileType").GetString() ?? string.Empty,
            FilterExpression: element.TryGetProperty("filter_expression", out var filter) ? filter.GetString() ?? string.Empty : string.Empty,
            InstalledHash: installedHash,
            InstalledSize: installedSize,
            DownloadHash: downloadHash,
            DownloadSize: downloadSize,
            Location: attachment.GetProperty("location").GetString() ?? string.Empty,
            InstalledFileName: installedFileName,
            DownloadFileName: downloadFileName);
    }

    private static string GetString(JsonElement element, string primaryName, string legacyName)
    {
        if (element.TryGetProperty(primaryName, out var primary))
        {
            return primary.GetString() ?? string.Empty;
        }

        if (element.TryGetProperty(legacyName, out var legacy))
        {
            return legacy.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static int ArchitecturePriority(string architecture) => architecture switch
    {
        "base" => 0,
        "base-memory" => 1,
        "tiny" => 2,
        _ => 3,
    };

    private static BergamotRemoteAsset ToAsset(RegistryRecord record) => new()
    {
        FileType = record.FileType,
        FileName = record.InstalledFileName,
        Url = AttachmentBaseUrl + record.Location.TrimStart('/'),
        Sha256 = record.InstalledHash,
        SizeBytes = record.InstalledSize,
        DownloadSha256 = record.DownloadHash,
        DownloadSizeBytes = record.DownloadSize,
        DownloadFileName = record.DownloadFileName,
        IsZstdCompressed = record.DownloadFileName.EndsWith(".zst", StringComparison.OrdinalIgnoreCase) ||
            record.Location.EndsWith(".zst", StringComparison.OrdinalIgnoreCase),
    };

    private sealed record RegistryRecord(
        string SourceLanguage,
        string TargetLanguage,
        string Architecture,
        string Version,
        string FileType,
        string FilterExpression,
        string InstalledHash,
        long InstalledSize,
        string DownloadHash,
        long DownloadSize,
        string Location,
        string InstalledFileName,
        string DownloadFileName);

    private sealed class VersionStringComparer : IComparer<string>
    {
        public static VersionStringComparer Instance { get; } = new();

        public int Compare(string? x, string? y)
        {
            if (Version.TryParse(x, out var xv) && Version.TryParse(y, out var yv))
            {
                return xv.CompareTo(yv);
            }

            return StringComparer.Ordinal.Compare(x, y);
        }
    }
}
