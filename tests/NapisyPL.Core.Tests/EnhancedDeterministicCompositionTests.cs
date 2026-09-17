namespace NapisyPL.Core.Tests;

public sealed class EnhancedDeterministicCompositionTests
{
    [Fact]
    public void EnhancedUi_IsSingleAudioGenderCorrectionSwitchWithoutReviewerSelectors()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "src", "NapisyPL", "MainWindow.axaml"));

        // Both switches live in the Options menu under short names.
        Assert.Contains("x:Name=\"EnhancedMenuItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Korekta rodzaju z głosu", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HardVoiceMenuItem\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Tylko pewne rozpoznanie", xaml, StringComparison.Ordinal);
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

    [Fact]
    public void EnhancedPipeline_TranslatesBeforeAudioAnalysis_AndQualityChecksAfterReview()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(root, "src", "NapisyPL.Core", "Services", "EnhancedTranslationPipeline.cs"));

        var translate = source.IndexOf("var translated = await TranslateNormallyAsync(", StringComparison.Ordinal);
        var analyzeAudio = source.IndexOf("var speakers = await speakerAnalysis.AnalyzeAsync(", StringComparison.Ordinal);
        var review = source.IndexOf("var reviewed = deterministicReview.Review(", StringComparison.Ordinal);
        var quality = source.IndexOf("FinalTranslationQualityGate", StringComparison.Ordinal);

        Assert.True(translate >= 0, "Enhanced pipeline must translate subtitles.");
        Assert.True(analyzeAudio >= 0, "Enhanced pipeline must analyze audio after translation.");
        Assert.True(review >= 0, "Enhanced pipeline must run deterministic gender review.");
        Assert.True(quality >= 0, "Enhanced pipeline must run the final EN↔PL quality gate.");
        Assert.True(translate < analyzeAudio, "MADLAD/provider translation must happen before audio gender analysis.");
        Assert.True(analyzeAudio < review, "Audio gender analysis must happen before gender review.");
        Assert.True(review < quality, "Final quality pass must happen after gender review.");
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
