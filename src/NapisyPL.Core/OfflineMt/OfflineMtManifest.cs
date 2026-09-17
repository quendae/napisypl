namespace NapisyPL.Core.OfflineMt;

public sealed record OfflineMtManifestFile(
    string RelativePath,
    string Sha256,
    long SizeBytes);

public sealed record OfflineMtManifest(
    string BackendId,
    string EngineVersion,
    string ModelId,
    string ModelVersion,
    string ModelSource,
    string LicenseId,
    bool BenchmarkOnly,
    long InstalledSizeBytes,
    DateTimeOffset InstalledAtUtc,
    IReadOnlyList<OfflineMtManifestFile> Files);
