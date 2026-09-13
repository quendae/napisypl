namespace NapisyPL.Core.Models;

public enum FolderFileStatus
{
    Translated,
    Skipped,
    Failed
}

public sealed record FolderFileResult(
    string InputPath,
    FolderFileStatus Status,
    string? OutputPath = null,
    string? ReasonCode = null);

public sealed record FolderBatchResult(
    int Translated,
    int Skipped,
    int Failed,
    IReadOnlyList<FolderFileResult> Files);
