using System.Text;
using System.Text.Json;

namespace NapisyPL.Core.Security;

public sealed class ApiKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public ApiKeyStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SubFlow",
                "secrets.json"),
            new WindowsDpapiSecretProtector())
    {
    }

    public ApiKeyStore(string path, ISecretProtector protector)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
    }

    public async Task<string?> LoadAsync(string provider, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadDictionaryAsync(cancellationToken);
            if (!values.TryGetValue(provider, out var encoded) || string.IsNullOrWhiteSpace(encoded))
                return null;

            try
            {
                var cipher = Convert.FromBase64String(encoded);
                return Encoding.UTF8.GetString(_protector.Unprotect(cipher));
            }
            catch
            {
                return null;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(string provider, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new ArgumentException("Provider is required.", nameof(provider));
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key is required.", nameof(apiKey));

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadDictionaryAsync(cancellationToken);
            var cipher = _protector.Protect(Encoding.UTF8.GetBytes(apiKey));
            values[provider] = Convert.ToBase64String(cipher);
            await SaveDictionaryAsync(values, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string provider, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var values = await LoadDictionaryAsync(cancellationToken);
            if (!values.Remove(provider))
                return;
            await SaveDictionaryAsync(values, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, string>> LoadDictionaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, string>(StringComparer.Ordinal);

            await using var stream = File.OpenRead(_path);
            var values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, JsonOptions, cancellationToken);
            return values is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(values, StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    private async Task SaveDictionaryAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, values, JsonOptions, cancellationToken);
        File.Move(temp, _path, overwrite: true);
    }
}
