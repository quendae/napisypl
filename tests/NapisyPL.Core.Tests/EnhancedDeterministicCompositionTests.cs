namespace NapisyPL.Core.Tests;

public sealed class EnhancedDeterministicCompositionTests
{
    [Fact]
    public void EnhancedUi_IsSingleAudioGenderCorrectionSwitchWithoutReviewerSelectors()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml"));

        Assert.Contains("Enhanced — popraw kontekst i rodzaj z audio", xaml, StringComparison.Ordinal);
        Assert.Contains("deterministycz", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EnhancedModelComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("EnhancedBackendComboBox", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Model korektora", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EnhancedPipeline_UsesDeterministicReviewWithoutReviewerLlmRuntime()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "NapisyPL.Core", "Services", "EnhancedTranslationPipeline.cs"));

        Assert.Contains("DeterministicGenderReviewService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalTargetedGenderReviewService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalContextRuntimeManager", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ArgosDisplayName", source, StringComparison.Ordinal);
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
