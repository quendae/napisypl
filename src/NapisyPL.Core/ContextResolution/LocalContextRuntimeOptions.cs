namespace NapisyPL.Core.ContextResolution;

public sealed record LocalContextRuntimeOptions(
    string BaseDirectory,
    string RuntimeZipUrl,
    string RuntimeVersion,
    string ModelUrl,
    string ModelFileName,
    int Port)
{
    public string RuntimeDirectory => Path.Combine(BaseDirectory, "runtime");
    public string ModelDirectory => Path.Combine(BaseDirectory, "models");
    public string ServerExecutablePath => Path.Combine(RuntimeDirectory, "llama-server.exe");
    public string ModelPath => Path.Combine(ModelDirectory, ModelFileName);
    public string BaseUrl => $"http://127.0.0.1:{Port}/v1";

    public static LocalContextRuntimeOptions CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDirectory = Path.Combine(localAppData, "SubFlow", "context-resolver");

        return new LocalContextRuntimeOptions(
            baseDirectory,
            "https://github.com/ggml-org/llama.cpp/releases/download/b10809/llama-b10809-bin-win-cpu-x64.zip",
            "b10809",
            "https://huggingface.co/ggml-org/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q4_K_M.gguf?download=true",
            "Qwen3-1.7B-Q4_K_M.gguf",
            17843);
    }

    public IReadOnlyList<string> BuildServerArguments(string modelPath) =>
    [
        "--model", modelPath,
        "--alias", "qwen3-1.7b",
        "--host", "127.0.0.1",
        "--port", Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "--ctx-size", "8192",
        "--parallel", "1",
        "--reasoning", "off",
        "--no-ui"
    ];
}
