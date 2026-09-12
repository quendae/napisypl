namespace NapisyPL.Core.OfflineMt.Bergamot;

public static class OpusMarianAssetManager
{
    public static string ResolveArchiveEntryPath(string rootDirectory, string entryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryName);

        if (Path.IsPathRooted(entryName) || LooksLikeWindowsAbsolutePath(entryName))
        {
            throw new InvalidDataException("OPUS archive contains an absolute path.");
        }

        var root = Path.GetFullPath(rootDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedEntry = entryName
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var resolved = Path.GetFullPath(Path.Combine(rootDirectory, normalizedEntry));

        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("OPUS archive entry escapes the installation directory.");
        }

        return resolved;
    }

    private static bool LooksLikeWindowsAbsolutePath(string path) =>
        path.Length >= 3 &&
        char.IsLetter(path[0]) &&
        path[1] == ':' &&
        (path[2] == '\\' || path[2] == '/');
}
