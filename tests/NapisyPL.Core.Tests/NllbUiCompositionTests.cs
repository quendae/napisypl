namespace NapisyPL.Core.Tests;

public sealed class NllbUiCompositionTests
{
    [Fact]
    public void DesktopProviderList_ExposesNllbAsBenchmarkOnlyOfflineBackend()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.Argos.cs"));

        Assert.Contains("NLLB-200 600M (offline, benchmark)", source, StringComparison.Ordinal);
        Assert.Contains("Benchmark only · CC-BY-NC-4.0", source, StringComparison.Ordinal);
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
