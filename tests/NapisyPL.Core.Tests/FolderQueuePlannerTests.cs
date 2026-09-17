using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class FolderQueuePlannerTests
{
    [Fact]
    public void Plan_IncludesSupportedFilesOnlyAndDoesNotRecurse()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "a.mkv"), string.Empty);
            File.WriteAllText(Path.Combine(root, "b.srt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "notes.docx"), string.Empty);
            var child = Directory.CreateDirectory(Path.Combine(root, "Season2"));
            File.WriteAllText(Path.Combine(child.FullName, "c.mkv"), string.Empty);

            var queue = new FolderQueuePlanner().Plan(root, exportTxt: false);

            Assert.Equal(2, queue.Count);
            Assert.Equal(["a.mkv", "b.srt"], queue.Select(x => Path.GetFileName(x.InputPath)).ToArray());
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void Plan_ExcludesGeneratedPolishOutputs()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "episode.srt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "episode.pl.srt"), string.Empty);
            File.WriteAllText(Path.Combine(root, "episode.pl.txt"), string.Empty);

            var queue = new FolderQueuePlanner().Plan(root, exportTxt: true);

            var item = Assert.Single(queue);
            Assert.Equal("episode.srt", Path.GetFileName(item.InputPath));
            Assert.True(item.SkipExisting);
            Assert.Equal("episode.pl.srt", Path.GetFileName(item.ExpectedOutputPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void Plan_MarksExistingPrimaryOutputAsSkipped()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "movie.mkv"), string.Empty);
            File.WriteAllText(Path.Combine(root, "movie.pl.srt"), string.Empty);

            var item = Assert.Single(new FolderQueuePlanner().Plan(root, exportTxt: false));

            Assert.True(item.SkipExisting);
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void Plan_UsesTxtOutputForTxtInput()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "dialogue.txt"), "Hello");

            var item = Assert.Single(new FolderQueuePlanner().Plan(root, exportTxt: false));

            Assert.Equal("dialogue.pl.txt", Path.GetFileName(item.ExpectedOutputPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    [Fact]
    public void Plan_PrefersSidecarSubtitleWhenVideoWouldWriteSameOutput()
    {
        var root = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(root, "movie.mkv"), string.Empty);
            File.WriteAllText(Path.Combine(root, "movie.srt"), string.Empty);

            var queue = new FolderQueuePlanner().Plan(root, exportTxt: false);

            var item = Assert.Single(queue);
            Assert.Equal("movie.srt", Path.GetFileName(item.InputPath));
            Assert.Equal("movie.pl.srt", Path.GetFileName(item.ExpectedOutputPath));
        }
        finally
        {
            DeleteTempDirectory(root);
        }
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "NapisyPL-folder-planner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteTempDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch { }
    }
}
