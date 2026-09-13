namespace NapisyPL.Core.OfflineMt.Nllb;

public static class NllbRuntimeRegistry
{
    private static readonly object Sync = new();
    private static NllbTranslatorClient? _client;

    public static IOfflineMachineTranslatorClient GetOrCreate(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        lock (Sync)
        {
            if (_client is not null)
                return _client;

            var paths = OfflineMtPaths.CreateDefault();
            var assets = new OfflineMtAssetManager(paths.Nllb600mDirectory);
            var helperPath = Path.Combine(AppContext.BaseDirectory, "tools", "offline-mt", "nllb_helper.py");
            if (!File.Exists(helperPath))
                helperPath = Path.Combine(AppContext.BaseDirectory, "nllb_helper.py");

            var runtime = new NllbRuntimeManager(
                new NllbRuntimeOptions("python.exe", helperPath, TimeSpan.FromMinutes(5)),
                paths.Nllb600mDirectory);
            var installer = new NllbAssetManager(httpClient, assets);
            _client = new NllbTranslatorClient(assets, runtime, installer.InstallPinnedModelAsync);
            return _client;
        }
    }

    public static async ValueTask DisposeAsync()
    {
        NllbTranslatorClient? client;
        lock (Sync)
        {
            client = _client;
            _client = null;
        }

        if (client is not null)
            await client.DisposeAsync();
    }
}
