using NapisyPL.Settings;

var store = new SettingsStore();
var writes = Enumerable.Range(0, 32)
    .Select(index => store.SaveAsync(new AppSettings
    {
        Provider = $"Provider-{index}",
        Model = $"Model-{index}",
        BaseUrl = $"http://localhost/{index}",
        ExportTxt = index % 2 == 0
    }))
    .ToArray();

try
{
    await Task.WhenAll(writes);
}
catch (Exception ex)
{
    Console.Error.WriteLine("SETTINGS_RACE_FAILED");
    Console.Error.WriteLine(ex);
    return 1;
}

var loaded = await store.LoadAsync();
if (string.IsNullOrWhiteSpace(loaded.Provider) || !loaded.Provider.StartsWith("Provider-", StringComparison.Ordinal))
{
    Console.Error.WriteLine("SETTINGS_RACE_FAILED: persisted settings are invalid.");
    return 1;
}

Console.WriteLine($"SETTINGS_RACE_OK provider={loaded.Provider}");
return 0;
