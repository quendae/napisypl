using System.IO.Compression;

namespace NapisyPL.Core.Services;

public sealed class FfmpegManager
{
    private const string WindowsBuildUrl = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _toolsDirectory;

    public FfmpegManager(HttpClient httpClient, string? dataDirectory = null)
    {
        _httpClient = httpClient;
        var root = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NapisyPL");
        _toolsDirectory = Path.Combine(root, "ffmpeg");
    }

    public string FfmpegPath => Path.Combine(_toolsDirectory, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
    public string FfprobePath => Path.Combine(_toolsDirectory, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");

    public async Task EnsureAvailableAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default)
    {
        if (File.Exists(FfmpegPath) && File.Exists(FfprobePath))
            return;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(FfmpegPath) && File.Exists(FfprobePath))
                return;

            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Automatyczne pobieranie FFmpeg w tej wersji jest dostępne tylko na Windows.");

            status?.Report("Pobieram FFmpeg (tylko przy pierwszym użyciu)…");
            Directory.CreateDirectory(_toolsDirectory);
            var tempRoot = Path.Combine(Path.GetTempPath(), "NapisyPL-ffmpeg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var zipPath = Path.Combine(tempRoot, "ffmpeg.zip");
            var extractPath = Path.Combine(tempRoot, "extract");

            try
            {
                using var response = await _httpClient.GetAsync(WindowsBuildUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
                await using (var output = File.Create(zipPath))
                    await input.CopyToAsync(output, cancellationToken);

                ZipFile.ExtractToDirectory(zipPath, extractPath);
                var ffmpeg = Directory.EnumerateFiles(extractPath, "ffmpeg.exe", SearchOption.AllDirectories).FirstOrDefault();
                var ffprobe = Directory.EnumerateFiles(extractPath, "ffprobe.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (ffmpeg is null || ffprobe is null)
                    throw new InvalidDataException("Pobrane archiwum nie zawiera ffmpeg.exe i ffprobe.exe.");

                File.Copy(ffmpeg, FfmpegPath, overwrite: true);
                File.Copy(ffprobe, FfprobePath, overwrite: true);
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
