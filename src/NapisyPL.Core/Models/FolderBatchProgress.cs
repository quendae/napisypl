namespace NapisyPL.Core.Models;

public sealed record FolderBatchProgress(
    int FileIndex,
    int FileCount,
    string FileName,
    TranslationProgress? Translation,
    string Stage);
