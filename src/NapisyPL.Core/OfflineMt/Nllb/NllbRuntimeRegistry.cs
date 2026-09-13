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
            var helperExecutable = Path.Combine(
                AppContext.BaseDirectory,
                "nllb-runtime",
                "SubFlow.NllbHelper.exe");
            var runtime = new NllbRuntimeManager(
                new NllbRuntimeOptions(helperExecutable, string.Empty, TimeSpan.FromMinutes(10)),
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
