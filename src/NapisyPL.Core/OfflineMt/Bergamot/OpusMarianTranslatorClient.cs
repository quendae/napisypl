using System.Text.Json;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class OpusMarianTranslatorClient : IOfflineMachineTranslatorClient, IAsyncDisposable
{
    public const string BackendIdValue = "opus-marian-eng-pol-2021-02-19";
    private const string DisplayNameValue = "OPUS-MT / Marian EN→PL";
    private const string RuntimeName = "bergamot-translator";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly OfflineMtAssetManager _assets;
    private readonly BergamotRuntimeManager _runtime;
    private readonly Func<CancellationToken, Task> _installModelAsync;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineMtManifest? _manifest;
    private bool _disposed;

    public OpusMarianTranslatorClient(
        OfflineMtAssetManager assets,
        BergamotRuntimeManager runtime,
        Func<CancellationToken, Task> installModelAsync)
    {
        _assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _installModelAsync = installModelAsync ?? throw new ArgumentNullException(nameof(installModelAsync));
    }

    public string BackendId => BackendIdValue;
    public string DisplayName => DisplayNameValue;

    public async Task EnsureReadyAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_manifest is not null && _runtime.IsRunning)
                return;

            _manifest ??= await TryLoadInstalledManifestAsync(cancellationToken);
            if (_manifest is null)
            {
                status?.Report("OPUS-MT/Marian: installing local EN→PL model…");
                await _installModelAsync(cancellationToken);
                _manifest = await TryLoadInstalledManifestAsync(cancellationToken)
                    ?? throw new InvalidDataException(
                        "OPUS-MT/Marian model installation completed without a valid local manifest.");
            }

            await _runtime.EnsureReadyAsync(status, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ThrowIfDisposed();
        if (texts.Count == 0)
            return [];

        await EnsureReadyAsync(cancellationToken: cancellationToken);
        return await _runtime.TranslateAsync(texts, cancellationToken);
    }

    public OfflineMachineTranslatorInfo GetInfo()
    {
        ThrowIfDisposed();
        var manifest = _manifest
            ?? throw new InvalidOperationException("OPUS-MT/Marian translator is not ready yet.");

        var modelFile = manifest.Files.FirstOrDefault(file =>
                string.Equals(Path.GetFileName(file.RelativePath), "model.npz", StringComparison.OrdinalIgnoreCase))
            ?? manifest.Files.First(file =>
                !string.Equals(Path.GetFileName(file.RelativePath), "config.yml", StringComparison.OrdinalIgnoreCase));

        return new OfflineMachineTranslatorInfo(
            BackendId: manifest.BackendId,
            DisplayName: DisplayNameValue,
            ModelId: manifest.ModelId,
            ModelVersion: manifest.ModelVersion,
            ModelSource: manifest.ModelSource,
            ModelSha256: modelFile.Sha256,
            InstalledSizeBytes: manifest.InstalledSizeBytes,
            RuntimeName: RuntimeName,
            RuntimeVersion: manifest.EngineVersion,
            Device: "CPU",
            LicenseId: manifest.LicenseId,
            BenchmarkOnly: manifest.BenchmarkOnly);
    }

    private async Task<OfflineMtManifest?> TryLoadInstalledManifestAsync(
        CancellationToken cancellationToken)
    {
        if (!await _assets.IsInstalledAsync(cancellationToken) || !File.Exists(_assets.ManifestPath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_assets.ManifestPath, cancellationToken);
            var manifest = JsonSerializer.Deserialize<OfflineMtManifest>(json, JsonOptions);
            return manifest is not null &&
                   string.Equals(manifest.BackendId, BackendIdValue, StringComparison.Ordinal)
                ? manifest
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
                return;

            await _runtime.DisposeAsync();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
