namespace NapisyPL.Core.Tests;

public sealed class SubtitleSearchUiTests
{
    [Fact]
    public void MainWindowExposesAndRoutesSubtitleSearch()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml.cs"));
        var handler = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.SubtitleSearch.cs"));

        Assert.Contains("x:Name=\"SearchSubtitlesCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCheckedChanged=\"OnSearchSubtitlesChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new SubtitleAcquisitionPipeline", code, StringComparison.Ordinal);
        Assert.Contains("_subtitlePipeline.TranslateInteractiveAsync", code, StringComparison.Ordinal);
        Assert.Contains("AllowMissingSubtitleTracks", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowComposesInteractiveSubtitleAlternativesOnlyForSingleFiles()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml.cs"));
        var selector = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "SubtitleAlternativeSelector.cs"));

        Assert.Contains("new SubtitleCueReader", code, StringComparison.Ordinal);
        Assert.Contains("new SubtitleSynchronizationService", code, StringComparison.Ordinal);
        Assert.Contains("new SubtitleAlternativeSelector", code, StringComparison.Ordinal);
        Assert.Contains("TranslateInteractiveAsync", code, StringComparison.Ordinal);
        Assert.Contains("DownloadInteractiveAsync", selector, StringComparison.Ordinal);
        Assert.Contains("Pokrycie", selector, StringComparison.Ordinal);
        Assert.Contains("P90", selector, StringComparison.Ordinal);
        Assert.Contains("przesunięcie", selector, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("skala", selector, StringComparison.OrdinalIgnoreCase);
        var folderHandler = code[code.IndexOf("private async Task TranslateFolderAsync", StringComparison.Ordinal)..];
        Assert.DoesNotContain("TranslateInteractiveAsync", folderHandler, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NapisyPL.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
