namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed record OpusMarianModelDescriptor(
    string BackendId,
    string SourceLanguage,
    string TargetLanguage,
    string Release,
    string ArchiveUrl,
    string Preprocessing)
{
    public static OpusMarianModelDescriptor Pinned { get; } = new(
        BackendId: "opus-marian-eng-pol-2021-02-19",
        SourceLanguage: "eng",
        TargetLanguage: "pol",
        Release: "2021-02-19",
        ArchiveUrl: "https://object.pouta.csc.fi/Tatoeba-MT-models/eng-pol/opus-2021-02-19.zip",
        Preprocessing: "normalization + SentencePiece spm32k/spm32k");
}
