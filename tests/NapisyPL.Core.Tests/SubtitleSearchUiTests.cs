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
        Assert.Contains("_subtitlePipeline.TranslateAsync", code, StringComparison.Ordinal);
        Assert.Contains("AllowMissingSubtitleTracks", handler, StringComparison.Ordinal);
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
