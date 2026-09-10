using System.Text.Json;

namespace NapisyPL.Settings;

public sealed class SettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SettingsStore()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NapisyPL");
        _path = Path.Combine(directory, "settings.json");
    }

    public async Task<AppSettings> LoadAsync()
    {
        try
        {
            if (!File.Exists(_path)) return new AppSettings();
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, settings, JsonOptions);
    }
}
