using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public sealed class FolderQueuePlanner
{
    public IReadOnlyList<FolderQueueItem> Plan(string folder, bool exportTxt)
    {
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Nie można znaleźć folderu: {folder}");

        var candidates = Directory.EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .Where(TranslationPipeline.IsSupportedInput)
            .Where(path => !IsGeneratedPolishOutput(path))
            .Select(path => new Candidate(path, GetPrimaryOutputPath(path)))
            .GroupBy(item => item.OutputPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(item.InputPath)) ? 1 : 0)
                .ThenBy(item => item.InputPath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(item => item.InputPath, StringComparer.OrdinalIgnoreCase)
            .Select(item => new FolderQueueItem(
                item.InputPath,
                item.OutputPath,
                File.Exists(item.OutputPath)))
            .ToArray();

        return candidates;
    }

    private static bool IsGeneratedPolishOutput(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".pl.srt", StringComparison.OrdinalIgnoreCase) ||
               name.EndsWith(".pl.txt", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPrimaryOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var extension = Path.GetExtension(inputPath);
        return Path.Combine(
            directory,
            extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? stem + ".pl.txt"
                : stem + ".pl.srt");
    }

    private sealed record Candidate(string InputPath, string OutputPath);
}
