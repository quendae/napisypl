namespace NapisyPL.Core.Tests;

public sealed class NllbUiCompositionTests
{
    [Fact]
    public void DesktopProviderList_ExposesFastBalancedAndBothQualityBackends()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.Argos.cs"));

        Assert.Contains("NLLB 600M — Fast", source, StringComparison.Ordinal);
        Assert.Contains("NLLB 1.3B — Balanced", source, StringComparison.Ordinal);
        Assert.Contains("NLLB 3.3B — Quality Test", source, StringComparison.Ordinal);
        Assert.Contains("MADLAD-400 3B — Quality", source, StringComparison.Ordinal);
        // Short hints: NLLB's non-commercial licence stays visible, MADLAD is the recommended local model.
        Assert.Contains("Licencja niekomercyjna", source, StringComparison.Ordinal);
        Assert.Contains("Zalecany", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderSwitch_ReleasesPreviouslyLoadedOfflineMtModel()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.Argos.cs"));
        var handlerStart = source.IndexOf("private async Task SelectProviderAsync", StringComparison.Ordinal);
        var handlerEnd = source.IndexOf("private static string ProviderDescription", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && handlerEnd > handlerStart);

        var handler = source[handlerStart..handlerEnd];
        Assert.Contains("await NllbRuntimeRegistry.DisposeAsync();", handler, StringComparison.Ordinal);
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
