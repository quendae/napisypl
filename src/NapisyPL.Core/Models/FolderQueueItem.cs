namespace NapisyPL.Core.Models;

public sealed record FolderQueueItem(
    string InputPath,
    string ExpectedOutputPath,
    bool SkipExisting);
