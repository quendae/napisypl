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

        foreach (var group in records
                     .GroupBy(record => record.Version, StringComparer.Ordinal)
                     .OrderByDescending(group => group.Key, VersionStringComparer.Instance))
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
                ModelVersion = group.Key,
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
        return new RegistryRecord(
            SourceLanguage: element.GetProperty("fromLang").GetString() ?? string.Empty,
            TargetLanguage: element.GetProperty("toLang").GetString() ?? string.Empty,
            Version: element.GetProperty("version").GetString() ?? string.Empty,
            FileType: element.GetProperty("fileType").GetString() ?? string.Empty,
            FilterExpression: element.TryGetProperty("filter_expression", out var filter) ? filter.GetString() ?? string.Empty : string.Empty,
            Hash: attachment.GetProperty("hash").GetString() ?? string.Empty,
            Size: attachment.GetProperty("size").GetInt64(),
            Location: attachment.GetProperty("location").GetString() ?? string.Empty,
            FileName: attachment.GetProperty("filename").GetString() ?? string.Empty);
    }

    private static BergamotRemoteAsset ToAsset(RegistryRecord record) => new()
    {
        FileType = record.FileType,
        FileName = record.FileName,
        Url = AttachmentBaseUrl + record.Location.TrimStart('/'),
        Sha256 = record.Hash,
        SizeBytes = record.Size,
    };

    private sealed record RegistryRecord(
        string SourceLanguage,
        string TargetLanguage,
        string Version,
        string FileType,
        string FilterExpression,
        string Hash,
        long Size,
        string Location,
        string FileName);

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
