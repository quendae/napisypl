using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace NapisyPL.Core.Subtitles;

public interface IQnapiRuntime
{
    Task<string> EnsureAvailableAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);
}

public sealed record QnapiRuntimePackage(string Version, Uri DownloadUri, string Sha256);

public sealed class QnapiRuntimeManager : IQnapiRuntime
{
    public const string Version = "0.2.3";
    public const string DownloadUrl =
        "https://github.com/QNapi/qnapi/releases/download/0.2.3/QNapi-0.2.3-portable.zip";
    public const string Sha256 = "03CF7BF82565DE8B31745B1862F751EC9EE789F8E50AF41D361EE9246F99F827";

    public static readonly IReadOnlyList<string> RequiredFiles =
    [
        "qnapi.exe",
        "7za.exe",
        "MediaInfo.dll",
        "libgcc_s_dw2-1.dll",
        "libstdc++-6.dll",
        "libwinpthread-1.dll",
        "Qt5Core.dll",
        "Qt5Gui.dll",
        "Qt5Network.dll",
        "Qt5Widgets.dll",
        "Qt5Xml.dll",
        Path.Combine("platforms", "qwindows.dll")
    ];

    private static readonly SemaphoreSlim InstallGate = new(1, 1);
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly HttpClient _httpClient;
    private readonly QnapiRuntimePackage _package;
    private readonly string _rootDirectory;

    public QnapiRuntimeManager(
        HttpClient httpClient,
        string? rootDirectory = null,
        QnapiRuntimePackage? package = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _package = package ?? new QnapiRuntimePackage(Version, new Uri(DownloadUrl), Sha256);
        ValidatePackage(_package);

        var root = rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NapisyPL", "qnapi");
        _rootDirectory = Path.GetFullPath(root);
    }

    public string RuntimeDirectory => Path.Combine(_rootDirectory, _package.Version);
    public string ExecutablePath => Path.Combine(RuntimeDirectory, "qnapi.exe");

    public async Task<string> EnsureAvailableAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        await InstallGate.WaitAsync(cancellationToken);
        try
        {
            if (IsCompleteRuntime(RuntimeDirectory))
            {
                await WriteControlledConfigAsync(RuntimeDirectory, cancellationToken);
                return ExecutablePath;
            }

            status?.Report("Pobieram silnik wyszukiwania napisów QNapi…");
            Directory.CreateDirectory(_rootDirectory);
            var stagingDirectory = Path.Combine(_rootDirectory, ".install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingDirectory);

            try
            {
                var archivePath = Path.Combine(stagingDirectory, "qnapi.zip");
                await DownloadAsync(archivePath, cancellationToken);
                await VerifyHashAsync(archivePath, cancellationToken);

                status?.Report("Instaluję silnik QNapi…");
                var extractedDirectory = Path.Combine(stagingDirectory, "extracted");
                ZipFile.ExtractToDirectory(archivePath, extractedDirectory);

                var executable = Directory.EnumerateFiles(
                        extractedDirectory, "qnapi.exe", SearchOption.AllDirectories)
                    .SingleOrDefault()
                    ?? throw new InvalidDataException("Archiwum QNapi nie zawiera qnapi.exe.");
                var payloadDirectory = Path.GetDirectoryName(executable)
                    ?? throw new InvalidDataException("Nie można ustalić katalogu QNapi.");

                EnsureCompleteRuntime(payloadDirectory);
                var readyDirectory = Path.Combine(stagingDirectory, "ready");
                Directory.Move(payloadDirectory, readyDirectory);
                await WriteControlledConfigAsync(readyDirectory, cancellationToken);

                ReplaceRuntime(readyDirectory);
                return ExecutablePath;
            }
            finally
            {
                TryDeleteDirectory(stagingDirectory);
            }
        }
        finally
        {
            InstallGate.Release();
        }
    }

    private async Task DownloadAsync(string archivePath, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            _package.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private async Task VerifyHashAsync(string archivePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!actual.Equals(_package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pobrane archiwum QNapi ma nieprawidłową sumę SHA-256.");
    }

    private void ReplaceRuntime(string readyDirectory)
    {
        var oldDirectory = Path.Combine(_rootDirectory, ".old-" + Guid.NewGuid().ToString("N"));
        var hadOldRuntime = Directory.Exists(RuntimeDirectory);
        if (hadOldRuntime)
            Directory.Move(RuntimeDirectory, oldDirectory);

        try
        {
            Directory.Move(readyDirectory, RuntimeDirectory);
        }
        catch
        {
            if (hadOldRuntime && !Directory.Exists(RuntimeDirectory) && Directory.Exists(oldDirectory))
                Directory.Move(oldDirectory, RuntimeDirectory);
            throw;
        }

        TryDeleteDirectory(oldDirectory);
    }

    private static bool IsCompleteRuntime(string directory) =>
        Directory.Exists(directory) && RequiredFiles.All(file => File.Exists(Path.Combine(directory, file)));

    private static void EnsureCompleteRuntime(string directory)
    {
        var missing = RequiredFiles.Where(file => !File.Exists(Path.Combine(directory, file))).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("Archiwum QNapi nie zawiera wymaganych plików: " + string.Join(", ", missing));
    }

    private static async Task WriteControlledConfigAsync(string directory, CancellationToken cancellationToken)
    {
        var config = """
            [qnapi]
            firstrun=false
            ui_language=en
            no_backup=true
            quiet_batch=false
            search_policy=0
            download_policy=0
            post_processing=true
            encoding_method=1
            enc_from=windows-1250
            auto_detect_encoding=true
            enc_to=UTF-8
            show_all_encodings=false
            sub_format=srt
            sub_ext=
            skip_convert_ads=false
            remove_lines=false
            engines=NapiProjekt:on,OpenSubtitles:off,Napisy24:on
            """;
        await File.WriteAllTextAsync(Path.Combine(directory, "qnapi.ini"), config, Utf8WithoutBom, cancellationToken);
    }

    private static void ValidatePackage(QnapiRuntimePackage package)
    {
        if (string.IsNullOrWhiteSpace(package.Version) ||
            package.Version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            package.Version is "." or "..")
            throw new ArgumentException("Nieprawidłowa wersja pakietu QNapi.", nameof(package));
        if (!package.DownloadUri.IsAbsoluteUri || package.DownloadUri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Pakiet QNapi musi używać adresu HTTPS.", nameof(package));
        if (package.Sha256.Length != 64 || package.Sha256.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("Nieprawidłowa suma SHA-256 pakietu QNapi.", nameof(package));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // A stale private staging directory is harmless and can be retried later.
        }
    }
}
