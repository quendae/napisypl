using System.Runtime.InteropServices;

namespace NapisyPL.Core.ContextResolution;

/// <summary>
/// Windows 10 ships an old C:\Windows\System32\onnxruntime.dll. Once anything in the process
/// (a file dialog, a shell extension) loads it, sherpa-onnx binds to that copy by module name and
/// fails with ERROR_BAD_EXE_FORMAT. Loading our bundled copy first makes the name resolve to it.
/// </summary>
public static class SherpaOnnxNative
{
    private const string OnnxRuntimeFile = "onnxruntime.dll";
    private const string SherpaFile = "sherpa-onnx-c-api.dll";

    private static readonly Lazy<string?> LoadError = new(LoadCore, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Call as early as possible at startup; never throws.</summary>
    public static void Preload() => _ = LoadError.Value;

    /// <summary>
    /// Throws before any sherpa-onnx object is created, so a broken load cannot surface later
    /// from a finalizer (which would terminate the process).
    /// </summary>
    public static void EnsureLoaded()
    {
        if (LoadError.Value is { } error)
            throw new DllNotFoundException("Nie udało się załadować rozpoznawania mówców (sherpa-onnx): " + error);
    }

    private static string? LoadCore()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var directory = FindNativeDirectory();
        if (directory is null)
            return null; // Leave it to the default DllImport probing.

        try
        {
            NativeLibrary.Load(Path.Combine(directory, OnnxRuntimeFile));
            var sherpa = NativeLibrary.Load(Path.Combine(directory, SherpaFile));
            return NativeLibrary.TryGetExport(sherpa, "SherpaOnnxCreateOfflineSpeakerDiarization", out _)
                ? null
                : "brak funkcji SherpaOnnxCreateOfflineSpeakerDiarization.";
        }
        catch (Exception exception) when (exception is DllNotFoundException or BadImageFormatException)
        {
            return exception.Message;
        }
    }

    private static string? FindNativeDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var architecture = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
        string[] candidates =
        [
            baseDirectory,
            Path.Combine(baseDirectory, "runtimes", "win-" + architecture, "native")
        ];

        return candidates.FirstOrDefault(candidate =>
            File.Exists(Path.Combine(candidate, OnnxRuntimeFile)) &&
            File.Exists(Path.Combine(candidate, SherpaFile)));
    }
}
