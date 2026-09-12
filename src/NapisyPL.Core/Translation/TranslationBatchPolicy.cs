namespace NapisyPL.Core.Translation;

public sealed record TranslationBatchPolicy(int MaxSegments, int MaxCharacters)
{
    public static TranslationBatchPolicy LlmDefault { get; } = new(20, 6000);
    public static TranslationBatchPolicy LocalLlm { get; } = new(10, 3500);
    public static TranslationBatchPolicy MachineTranslation { get; } = new(40, 12000);
}
