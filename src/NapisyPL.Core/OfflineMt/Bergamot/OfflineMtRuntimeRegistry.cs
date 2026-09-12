namespace NapisyPL.Core.OfflineMt.Bergamot;

public static class OfflineMtRuntimeRegistry
{
    private static readonly object Sync = new();
    private static FirefoxBergamotTranslatorClient? _firefox;
    private static OpusMarianTranslatorClient? _opus;

    public static IOfflineMachineTranslatorClient GetFirefox(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        lock (Sync)
        {
            if (_firefox is not null)
                return _firefox;

            var paths = OfflineMtPaths.CreateDefault();
            var assets = new OfflineMtAssetManager(paths.BergamotDirectory);
            var runtime = CreateRuntime(paths.BergamotDirectory);
            _firefox = new FirefoxBergamotTranslatorClient(httpClient, assets, runtime);
            return _firefox;
        }
    }

    public static IOfflineMachineTranslatorClient GetOpusMarian(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        lock (Sync)
        {
            if (_opus is not null)
                return _opus;

            var paths = OfflineMtPaths.CreateDefault();
            var assets = new OfflineMtAssetManager(paths.OpusMarianDirectory);
            var runtime = CreateRuntime(paths.OpusMarianDirectory);
            _opus = new OpusMarianTranslatorClient(httpClient, assets, runtime);
            return _opus;
        }
    }

    public static async ValueTask DisposeAsync()
    {
        FirefoxBergamotTranslatorClient? firefox;
        OpusMarianTranslatorClient? opus;
        lock (Sync)
        {
            firefox = _firefox;
            opus = _opus;
            _firefox = null;
            _opus = null;
        }

        if (firefox is not null)
            await firefox.DisposeAsync();
        if (opus is not null)
            await opus.DisposeAsync();
    }

    private static BergamotRuntimeManager CreateRuntime(string modelDirectory)
    {
        var helperPath = Path.Combine(AppContext.BaseDirectory, "SubFlow.BergamotHelper.exe");
        return new BergamotRuntimeManager(new BergamotRuntimeOptions(helperPath), modelDirectory);
    }
}
