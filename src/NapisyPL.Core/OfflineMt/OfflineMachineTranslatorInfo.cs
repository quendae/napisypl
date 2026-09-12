namespace NapisyPL.Core.OfflineMt;

public sealed record OfflineMachineTranslatorInfo(
    string BackendId,
    string DisplayName,
    string ModelId,
    string ModelVersion,
    string ModelSource,
    string ModelSha256,
    long InstalledSizeBytes,
    string RuntimeName,
    string RuntimeVersion,
    string Device,
    string LicenseId,
    bool BenchmarkOnly);
