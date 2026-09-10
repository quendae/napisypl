using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class LocalContextRuntimeOptionsTests
{
    [Fact]
    public void Default_UsesPinnedWindowsCpuRuntimeAndSmallQwenModel()
    {
        var options = LocalContextRuntimeOptions.CreateDefault();

        Assert.Contains("SubFlow", options.BaseDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("b10809", options.RuntimeZipUrl);
        Assert.EndsWith("llama-b10809-bin-win-cpu-x64.zip", options.RuntimeZipUrl);
        Assert.Contains("ggml-org/Qwen3-1.7B-GGUF", options.ModelUrl);
        Assert.EndsWith("Qwen3-1.7B-Q4_K_M.gguf?download=true", options.ModelUrl);
        Assert.Equal(17843, options.Port);
    }

    [Fact]
    public void BuildServerArguments_BindsOnlyToLocalhostAndDisablesReasoningAndUi()
    {
        var options = LocalContextRuntimeOptions.CreateDefault();
        var args = options.BuildServerArguments(@"C:\models\resolver.gguf");
        var joined = string.Join(" ", args);

        Assert.Contains("--host 127.0.0.1", joined);
        Assert.Contains("--port 17843", joined);
        Assert.Contains("--ctx-size 8192", joined);
        Assert.Contains("--reasoning off", joined);
        Assert.Contains("--no-ui", joined);
        Assert.Contains("--parallel 1", joined);
        Assert.Contains(@"C:\models\resolver.gguf", joined);
    }
}
