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
    public void OfflineMtGpuProfilesWindowsPackage_ContainsCpuFallbackAndAmdRuntimeInstaller()
    {
        var root = FindRepositoryRoot();
        var workflowPath = Path.Combine(root, ".github", "workflows", "package-nllb.yml");
        var registryPath = Path.Combine(root, "src", "NapisyPL.Core", "OfflineMt", "Nllb", "NllbRuntimeRegistry.cs");
        var amdInstallerPath = Path.Combine(root, "tools", "offline-mt", "install-amd-runtime.ps1");
        var amdInstallerCmdPath = Path.Combine(root, "tools", "offline-mt", "install-amd-runtime.cmd");

        Assert.True(File.Exists(workflowPath), $"Missing offline MT package workflow: {workflowPath}");
        Assert.True(File.Exists(amdInstallerPath), $"Missing AMD runtime installer: {amdInstallerPath}");
        Assert.True(File.Exists(amdInstallerCmdPath), $"Missing AMD runtime installer wrapper: {amdInstallerCmdPath}");

        var workflow = File.ReadAllText(workflowPath);
        Assert.Contains("PyInstaller", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SubFlow.NllbHelper.exe", workflow, StringComparison.Ordinal);
        Assert.Contains("NLLB_SMOKE_OK", workflow, StringComparison.Ordinal);
        Assert.Contains("Test offline MT helper protocol", workflow, StringComparison.Ordinal);
        Assert.Contains("Install-AMD-GPU-Runtime.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("Install-AMD-GPU-Runtime.cmd", workflow, StringComparison.Ordinal);
        Assert.Contains("SubFlow-win-x64-offline-mt-gpu-profiles-test", workflow, StringComparison.Ordinal);

        var registry = File.ReadAllText(registryPath);
        Assert.Contains("SubFlow.NllbHelper.exe", registry, StringComparison.Ordinal);
        Assert.Contains("nllb-amd-runtime", registry, StringComparison.Ordinal);
        Assert.Contains("python.exe", registry, StringComparison.Ordinal);

        var amdInstaller = File.ReadAllText(amdInstallerPath);
        Assert.Contains("device-gfx1030", amdInstaller, StringComparison.Ordinal);
        Assert.Contains("api.nuget.org/v3-flatcontainer/python", amdInstaller, StringComparison.Ordinal);
        Assert.Contains("rocm.nightlies.amd.com/whl-multi-arch", amdInstaller, StringComparison.Ordinal);
        Assert.Contains("2.13.0+rocm10.1.0a20260822", amdInstaller, StringComparison.Ordinal);
        Assert.Contains("-m pip --version", amdInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("bootstrap.pypa.io/get-pip.py", amdInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("python.org/ftp/python", amdInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("stable.repo.amd.com/rocm/whl-next", amdInstaller, StringComparison.Ordinal);
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
