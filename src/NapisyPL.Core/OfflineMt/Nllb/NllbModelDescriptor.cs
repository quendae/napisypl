namespace NapisyPL.Core.OfflineMt.Nllb;

public sealed record NllbModelFile(
    string RelativePath,
    string DownloadUrl);

public sealed record NllbModelDescriptor(
    string BackendId,
    string ModelId,
    string Revision,
    string SourceLanguage,
    string TargetLanguage,
    string LicenseId,
    bool BenchmarkOnly,
    IReadOnlyList<NllbModelFile> Files)
{
    private const string PinnedRevision = "f8d333a098d19b4fd9a8b18f94170487ad3f821d";
    private const string RepositoryBase = "https://huggingface.co/facebook/nllb-200-distilled-600M";

    public static NllbModelDescriptor Pinned { get; } = new(
        BackendId: "nllb-200-distilled-600m-eng-pol",
        ModelId: "facebook/nllb-200-distilled-600M",
        Revision: PinnedRevision,
        SourceLanguage: "eng_Latn",
        TargetLanguage: "pol_Latn",
        LicenseId: "CC-BY-NC-4.0",
        BenchmarkOnly: true,
        Files:
        [
            File("config.json"),
            File("generation_config.json"),
            File("pytorch_model.bin"),
            File("sentencepiece.bpe.model"),
            File("special_tokens_map.json"),
            File("tokenizer.json"),
            File("tokenizer_config.json")
        ]);

    public string RepositoryTreeUrl => $"{RepositoryBase}/tree/{Revision}";

    private static NllbModelFile File(string relativePath) =>
        new(relativePath, $"{RepositoryBase}/resolve/{PinnedRevision}/{relativePath}?download=true");
}
