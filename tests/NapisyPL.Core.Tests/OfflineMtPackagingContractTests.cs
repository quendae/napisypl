namespace NapisyPL.Core.Tests;

public sealed class OfflineMtPackagingContractTests
{
    [Fact]
    public void WindowsCi_BuildsAndPackagesBergamotHelperFromSharedScript()
    {
        var root = FindRepositoryRoot();
        var buildScript = Path.Combine(root, "tools", "offline-mt", "build-bergamot-helper.ps1");
        var ciPath = Path.Combine(root, ".github", "workflows", "ci.yml");
        var runtimeWorkflowPath = Path.Combine(root, ".github", "workflows", "offline-mt-runtime.yml");

        Assert.True(File.Exists(buildScript), $"Missing shared Bergamot build script: {buildScript}");

        var ci = File.ReadAllText(ciPath);
        Assert.Contains("tools/offline-mt/build-bergamot-helper.ps1", ci, StringComparison.Ordinal);
        Assert.Contains("SubFlow.BergamotHelper.exe", ci, StringComparison.Ordinal);
        Assert.Contains("artifacts/win-x64/SubFlow.BergamotHelper.exe", ci, StringComparison.Ordinal);

        var runtimeWorkflow = File.ReadAllText(runtimeWorkflowPath);
        Assert.Contains("tools/offline-mt/build-bergamot-helper.ps1", runtimeWorkflow, StringComparison.Ordinal);
    }

    [Fact]
    public void NllbWindowsPackage_IsSelfContainedAndRealModelSmokeTested()
    {
        var root = FindRepositoryRoot();
        var workflowPath = Path.Combine(root, ".github", "workflows", "package-nllb.yml");
        var registryPath = Path.Combine(root, "src", "NapisyPL.Core", "OfflineMt", "Nllb", "NllbRuntimeRegistry.cs");

        Assert.True(File.Exists(workflowPath), $"Missing NLLB package workflow: {workflowPath}");
        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("PyInstaller", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SubFlow.NllbHelper.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("NLLB_SMOKE_OK", workflow, StringComparison.Ordinal);
        Assert.Contains("SubFlow-win-x64-nllb-test", workflow, StringComparison.Ordinal);

        var registry = File.ReadAllText(registryPath);
        Assert.Contains("SubFlow.NllbHelper.exe", registry, StringComparison.Ordinal);
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
