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
        var services = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "AppServices.cs"));

        Assert.Contains("x:Name=\"SearchSubtitlesCheckBox\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCheckedChanged=\"OnSearchSubtitlesChanged\"", xaml, StringComparison.Ordinal);
        Assert.Contains("new SubtitleAcquisitionPipeline", services, StringComparison.Ordinal);
        Assert.Contains("_services.SubtitlePipeline", code, StringComparison.Ordinal);
        Assert.Contains("_subtitlePipeline.TranslateInteractiveAsync", code, StringComparison.Ordinal);
        Assert.Contains("AllowMissingSubtitleTracks", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowComposesInteractiveSubtitleAlternativesOnlyForSingleFiles()
    {
        var root = FindRepositoryRoot();
        var code = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml.cs"));
        var services = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "AppServices.cs"));
        var selector = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "SubtitleAlternativeSelector.cs"));

        // The shared services compose timing checks; the window adds the interactive selector.
        Assert.Contains("new SubtitleCueReader", services, StringComparison.Ordinal);
        Assert.Contains("new SubtitleSynchronizationService", services, StringComparison.Ordinal);
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

    [Fact]
    public void AlternativeSelectorHandlesDismissalAndAsyncActionFailures()
    {
        var root = FindRepositoryRoot();
        var selector = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "SubtitleAlternativeSelector.cs"));
        var downloader = File.ReadAllText(Path.Combine(root, "src", "NapisyPL.Core", "Subtitles", "QnapiSubtitleDownloader.cs"));

        Assert.Contains("?? new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel)", selector, StringComparison.Ordinal);
        Assert.Contains("RunActionAsync", selector, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", selector, StringComparison.Ordinal);
        Assert.Contains("dialog.IsVisible", selector, StringComparison.Ordinal);
        Assert.Contains("new CancellationTokenSource(interactive ? InteractiveProcessTimeout : _processTimeout)", downloader, StringComparison.Ordinal);
        Assert.Contains("ContinueWithEnglishFallback", selector, StringComparison.Ordinal);
        Assert.Contains("CreateLinkedTokenSource", selector, StringComparison.Ordinal);
        Assert.Contains("activeActionCancellation?.Cancel()", selector, StringComparison.Ordinal);
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
