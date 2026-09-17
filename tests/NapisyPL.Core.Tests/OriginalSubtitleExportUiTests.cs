namespace NapisyPL.Core.Tests;

public sealed class OriginalSubtitleExportUiTests
{
    [Fact]
    public void TrackPanel_OffersOriginalSubtitleExtractionWithoutTranslationProvider()
    {
        var root = FindRepositoryRoot();
        var axaml = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.OriginalSubtitles.cs"));

        Assert.Contains("x:Name=\"ExtractOriginalSubtitleButton\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Zapisz oryginalne napisy z filmu\"", axaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"OnExtractOriginalSubtitleClick\"", axaml, StringComparison.Ordinal);
        Assert.Contains("OriginalSubtitleExportPath.Build", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OriginalSubtitleExportService", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ExportAsync", codeBehind, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var candidates = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
        foreach (var candidate in candidates)
        {
            var directory = new DirectoryInfo(candidate);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "NapisyPL.sln")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Could not locate repository root containing NapisyPL.sln.");
    }
}
