using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class LocalContextRuntimeOptionsTests
{
    [Fact]
    public void Default_PrefersAutoVulkanAndQwen35NineB()
    {
        var options = LocalContextRuntimeOptions.CreateDefault();

        Assert.Equal(LocalContextBackend.Auto, options.Backend);
        Assert.Equal(LocalContextModelKind.Qwen35_9B, options.ModelKind);
        Assert.Contains("SubFlow", options.BaseDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b10809", options.RuntimeZipUrl);
        Assert.EndsWith("llama-b10809-bin-win-vulkan-x64.zip", options.RuntimeZipUrl);
        Assert.Contains("openresearchtools/Qwen3.5-9B-GGUF", options.ModelUrl);
        Assert.Equal("Qwen3.5-9B-Q4_K_M.gguf", options.ModelFileName);
        Assert.Equal("qwen3.5-9b", options.ModelAlias);
        Assert.Equal(17843, options.Port);
    }

    [Theory]
    [InlineData(LocalContextModelKind.Qwen35_9B, "qwen3.5-9b", "Qwen3.5-9B-Q4_K_M.gguf")]
    [InlineData(LocalContextModelKind.GptOss20B, "gpt-oss-20b", "gpt-oss-20b-mxfp4.gguf")]
    [InlineData(LocalContextModelKind.Qwen3_1_7B, "qwen3-1.7b", "Qwen3-1.7B-Q4_K_M.gguf")]
    public void Create_SelectsRequestedModelProfile(
        LocalContextModelKind kind,
        string alias,
        string fileName)
    {
        var options = LocalContextRuntimeOptions.Create(LocalContextBackend.Cpu, kind);

        Assert.Equal(kind, options.ModelKind);
        Assert.Equal(alias, options.ModelAlias);
        Assert.Equal(fileName, options.ModelFileName);
    }

    [Fact]
    public void BuildServerArguments_CpuDisablesGpuOffload()
    {
        var options = LocalContextRuntimeOptions.Create(LocalContextBackend.Cpu, LocalContextModelKind.Qwen35_9B);
        var joined = string.Join(" ", options.BuildServerArguments(@"C:\models\resolver.gguf"));

        Assert.Contains("--device none", joined);
        Assert.DoesNotContain("--gpu-layers all", joined);
    }

    [Fact]
    public void BuildServerArguments_VulkanPinsResolvedDeviceAndOffloadsAllLayers()
    {
        var options = LocalContextRuntimeOptions.Create(LocalContextBackend.Vulkan, LocalContextModelKind.Qwen35_9B);
        var joined = string.Join(" ", options.BuildServerArguments(@"C:\models\resolver.gguf", "Vulkan1"));

        Assert.Contains("--device Vulkan1", joined);
        Assert.Contains("--gpu-layers all", joined);
        Assert.Contains("--split-mode none", joined);
    }

    [Fact]
    public void BuildServerArguments_BindsOnlyToLocalhostAndDisablesUi()
    {
        var options = LocalContextRuntimeOptions.Create(LocalContextBackend.Cpu, LocalContextModelKind.Qwen35_9B);
        var joined = string.Join(" ", options.BuildServerArguments(@"C:\models\resolver.gguf"));

        Assert.Contains("--host 127.0.0.1", joined);
        Assert.Contains("--port 17843", joined);
        Assert.Contains("--ctx-size 8192", joined);
        Assert.Contains("--no-ui", joined);
        Assert.Contains("--parallel 1", joined);
        Assert.Contains(@"C:\models\resolver.gguf", joined);
    }

    [Fact]
    public void SelectPreferredVulkanDevice_PrefersAmdRadeon()
    {
        const string output = "Vulkan0: NVIDIA GeForce RTX 3060\nVulkan1: AMD Radeon RX 6950 XT\n";

        Assert.Equal("Vulkan1", LocalContextDeviceSelector.SelectPreferredVulkanDevice(output));
    }

    [Fact]
    public void SelectPreferredVulkanDevice_UsesFirstVulkanDeviceWhenNoAmdExists()
    {
        const string output = "Vulkan0: Intel Arc\nVulkan1: NVIDIA GeForce RTX 3060\n";

        Assert.Equal("Vulkan0", LocalContextDeviceSelector.SelectPreferredVulkanDevice(output));
    }
}
