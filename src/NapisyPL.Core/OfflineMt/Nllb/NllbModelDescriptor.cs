namespace NapisyPL.Core.OfflineMt.Nllb;

public enum NllbModelProfile
{
    Fast600M,
    Balanced1_3B,
    QualityNllb3_3B,
    QualityMadlad3B
}

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
    private const string FastRevision = "f8d333a098d19b4fd9a8b18f94170487ad3f821d";
    private const string BalancedRevision = "e43ee79ff1768201e83dea963fcb082f47d6eb17";
    private const string NllbQualityRevision = "1a07f7d195896b2114afcb79b7b57ab512e7b43e";
    private const string MadladQualityRevision = "fa184c675da0b5c9e1c8694fccd4e12e2d422094";

    public static NllbModelDescriptor Fast600M { get; } = HuggingFace(
        backendId: "nllb-200-distilled-600m-eng-pol",
        modelId: "facebook/nllb-200-distilled-600M",
        revision: FastRevision,
        sourceLanguage: "eng_Latn",
        targetLanguage: "pol_Latn",
        licenseId: "CC-BY-NC-4.0",
        benchmarkOnly: true,
        files:
        [
            "config.json",
            "generation_config.json",
            "pytorch_model.bin",
            "sentencepiece.bpe.model",
            "special_tokens_map.json",
            "tokenizer.json",
            "tokenizer_config.json"
        ]);

    public static NllbModelDescriptor Balanced1_3B { get; } = HuggingFace(
        backendId: "nllb-200-distilled-1.3b-eng-pol",
        modelId: "facebook/nllb-200-distilled-1.3B",
        revision: BalancedRevision,
        sourceLanguage: "eng_Latn",
        targetLanguage: "pol_Latn",
        licenseId: "CC-BY-NC-4.0",
        benchmarkOnly: true,
        files:
        [
            "config.json",
            "generation_config.json",
            "pytorch_model.bin",
            "sentencepiece.bpe.model",
            "special_tokens_map.json",
            "tokenizer.json",
            "tokenizer_config.json"
        ]);

    public static NllbModelDescriptor QualityNllb3_3B { get; } = HuggingFace(
        backendId: "nllb-200-3.3b-eng-pol",
        modelId: "facebook/nllb-200-3.3B",
        revision: NllbQualityRevision,
        sourceLanguage: "eng_Latn",
        targetLanguage: "pol_Latn",
        licenseId: "CC-BY-NC-4.0",
        benchmarkOnly: true,
        files:
        [
            "config.json",
            "generation_config.json",
            "pytorch_model-00001-of-00003.bin",
            "pytorch_model-00002-of-00003.bin",
            "pytorch_model-00003-of-00003.bin",
            "pytorch_model.bin.index.json",
            "sentencepiece.bpe.model",
            "special_tokens_map.json",
            "tokenizer.json",
            "tokenizer_config.json"
        ]);

    public static NllbModelDescriptor QualityMadlad3B { get; } = HuggingFace(
        backendId: "madlad-400-3b-eng-pol",
        modelId: "google/madlad400-3b-mt",
        revision: MadladQualityRevision,
        sourceLanguage: "en",
        targetLanguage: "pl",
        licenseId: "Apache-2.0",
        benchmarkOnly: false,
        files:
        [
            "added_tokens.json",
            "config.json",
            "generation_config.json",
            "model.safetensors",
            "special_tokens_map.json",
            "spiece.model",
            "tokenizer.json",
            "tokenizer_config.json"
        ]);

    public static NllbModelDescriptor Pinned => Fast600M;

    public static NllbModelDescriptor ForProfile(NllbModelProfile profile) => profile switch
    {
        NllbModelProfile.Fast600M => Fast600M,
        NllbModelProfile.Balanced1_3B => Balanced1_3B,
        NllbModelProfile.QualityNllb3_3B => QualityNllb3_3B,
        NllbModelProfile.QualityMadlad3B => QualityMadlad3B,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown offline MT model profile.")
    };

    public string RepositoryTreeUrl => $"https://huggingface.co/{ModelId}/tree/{Revision}";

    private static NllbModelDescriptor HuggingFace(
        string backendId,
        string modelId,
        string revision,
        string sourceLanguage,
        string targetLanguage,
        string licenseId,
        bool benchmarkOnly,
        IReadOnlyList<string> files) =>
        new(
            backendId,
            modelId,
            revision,
            sourceLanguage,
            targetLanguage,
            licenseId,
            benchmarkOnly,
            files.Select(path => new NllbModelFile(
                path,
                $"https://huggingface.co/{modelId}/resolve/{revision}/{path}?download=true")).ToArray());
}
