namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class BergamotRemoteAsset
{
    public required string FileType { get; init; }
    public required string FileName { get; init; }
    public required string Url { get; init; }
    public required string Sha256 { get; init; }
    public required long SizeBytes { get; init; }
}

public sealed class BergamotModelDescriptor
{
    public required string SourceLanguage { get; init; }
    public required string TargetLanguage { get; init; }
    public required string ModelVersion { get; init; }
    public required BergamotRemoteAsset Model { get; init; }
    public required BergamotRemoteAsset SourceVocab { get; init; }
    public required BergamotRemoteAsset TargetVocab { get; init; }
    public required BergamotRemoteAsset Shortlist { get; init; }
}
