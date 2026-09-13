using System.Text.RegularExpressions;

namespace NapisyPL.Core.ContextResolution;

public enum LocalContextBackend
{
    Auto,
    Vulkan,
    Cpu
}

public enum LocalContextModelKind
{
    Qwen35_9B,
    GptOss20B,
    Qwen3_1_7B
}

public sealed record LocalContextRuntimeOptions(
    string BaseDirectory,
    string RuntimeZipUrl,
    string RuntimeVersion,
    LocalContextBackend Backend,
    LocalContextModelKind ModelKind,
    string ModelUrl,
    string ModelFileName,
    string ModelAlias,
    string ModelDisplayName,
    string ModelApproxSize,
    int Port)
{
    private const string RuntimeVersionValue = "b10809";
    private const string RuntimeReleaseBase = "https://github.com/ggml-org/llama.cpp/releases/download/b10809";

    public string RuntimeFlavor => Backend == LocalContextBackend.Cpu ? "cpu" : "vulkan";
    public string RuntimeDirectory => Path.Combine(BaseDirectory, "runtime", RuntimeVersion, RuntimeFlavor);
    public string ModelDirectory => Path.Combine(BaseDirectory, "models");
    public string ServerExecutablePath => Path.Combine(RuntimeDirectory, "llama-server.exe");
    public string ModelPath => Path.Combine(ModelDirectory, ModelFileName);
    public string BaseUrl => $"http://127.0.0.1:{Port}/v1";

    public static LocalContextRuntimeOptions CreateDefault() =>
        Create(LocalContextBackend.Auto, LocalContextModelKind.Qwen35_9B);

    public static LocalContextRuntimeOptions Create(
        LocalContextBackend backend,
        LocalContextModelKind modelKind)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = Path.Combine(localAppData, "SubFlow", "context-resolver");
        var runtimeZipUrl = backend == LocalContextBackend.Cpu
            ? $"{RuntimeReleaseBase}/llama-{RuntimeVersionValue}-bin-win-cpu-x64.zip"
            : $"{RuntimeReleaseBase}/llama-{RuntimeVersionValue}-bin-win-vulkan-x64.zip";

        var profile = modelKind switch
        {
            LocalContextModelKind.Qwen35_9B => new LocalContextModelProfile(
                "https://huggingface.co/openresearchtools/Qwen3.5-9B-GGUF/resolve/main/Qwen3.5-9B-Q4_K_M.gguf?download=true",
                "Qwen3.5-9B-Q4_K_M.gguf",
                "qwen3.5-9b",
                "Qwen 3.5 9B Q4_K_M",
                "~5.6 GB"),
            LocalContextModelKind.GptOss20B => new LocalContextModelProfile(
                "https://huggingface.co/recursechat/gpt-oss-20b-GGUF/resolve/main/gpt-oss-20b-mxfp4.gguf?download=true",
                "gpt-oss-20b-mxfp4.gguf",
                "gpt-oss-20b",
                "GPT-OSS 20B MXFP4",
                "~12.1 GB"),
            LocalContextModelKind.Qwen3_1_7B => new LocalContextModelProfile(
                "https://huggingface.co/ggml-org/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf?download=true",
                "Qwen3-1.7B-Q4_K_M.gguf",
                "qwen3-1.7b",
                "Qwen 3 1.7B Q4_K_M",
                "~1.3 GB"),
            _ => throw new ArgumentOutOfRangeException(nameof(modelKind))
        };

        return new LocalContextRuntimeOptions(
            baseDirectory,
            runtimeZipUrl,
            RuntimeVersionValue,
            backend,
            modelKind,
            profile.Url,
            profile.FileName,
            profile.Alias,
            profile.DisplayName,
            profile.ApproxSize,
            17843);
    }

    public LocalContextRuntimeOptions WithBackend(LocalContextBackend backend) =>
        Create(backend, ModelKind) with { BaseDirectory = BaseDirectory, Port = Port };

    public IReadOnlyList<string> BuildServerArguments(string modelPath, string? resolvedDevice = null)
    {
        var args = new List<string>
        {
            "--model", modelPath,
            "--alias", ModelAlias,
            "--host", "127.0.0.1",
            "--port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", "8192",
            "--parallel", "1",
            "--jinja",
            "--no-ui"
        };

        if (ModelKind == LocalContextModelKind.GptOss20B)
        {
            args.Add("--chat-template-kwargs");
            args.Add("{\"reasoning_effort\":\"low\"}");
        }
        else
        {
            args.Add("--reasoning");
            args.Add("off");
        }

        if (Backend == LocalContextBackend.Cpu)
        {
            args.Add("--device");
            args.Add("none");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(resolvedDevice))
                throw new InvalidOperationException("Vulkan backend requires a resolved llama.cpp device.");

            args.Add("--device");
            args.Add(resolvedDevice);
            args.Add("--gpu-layers");
            args.Add("all");
            args.Add("--split-mode");
            args.Add("none");
        }

        return args;
    }

    private sealed record LocalContextModelProfile(
        string Url,
        string FileName,
        string Alias,
        string DisplayName,
        string ApproxSize);
}

public static partial class LocalContextDeviceSelector
{
    public static string? SelectPreferredVulkanDevice(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var matches = VulkanDeviceRegex().Matches(output);
        if (matches.Count == 0)
            return null;

        foreach (Match match in matches)
        {
            var line = match.Value;
            if (line.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                return match.Groups[1].Value;
        }

        return matches[0].Groups[1].Value;
    }

    [GeneratedRegex(@"(?im)\b(Vulkan\d+)\b[^\r\n]*")]
    private static partial Regex VulkanDeviceRegex();
}
