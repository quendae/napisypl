namespace NapisyPL.Core.Services;

public static class OriginalSubtitleExportPath
{
    public static string Build(string mediaPath)
    {
        if (string.IsNullOrWhiteSpace(mediaPath))
            throw new ArgumentException("Ścieżka pliku wideo nie może być pusta.", nameof(mediaPath));

        var directory = Path.GetDirectoryName(mediaPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(mediaPath) + ".srt";
        return Path.Combine(directory, fileName);
    }

}
