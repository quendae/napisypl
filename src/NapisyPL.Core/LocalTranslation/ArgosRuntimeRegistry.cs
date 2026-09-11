namespace NapisyPL.Core.LocalTranslation;

public static class ArgosRuntimeRegistry
{
    private static readonly object Sync = new();
    private static ArgosTranslationRuntimeManager? _runtime;

    public static ArgosTranslationRuntimeManager GetOrCreate(HttpClient httpClient)
    {
        lock (Sync)
        {
            if (_runtime is not null)
                return _runtime;

            var options = ArgosRuntimeOptions.CreateDefault();
            var assets = new ArgosModelAssetManager(httpClient, options);
            _runtime = new ArgosTranslationRuntimeManager(assets, options);
            return _runtime;
        }
    }

    public static async ValueTask DisposeAsync()
    {
        ArgosTranslationRuntimeManager? runtime;
        lock (Sync)
        {
            runtime = _runtime;
            _runtime = null;
        }

        if (runtime is not null)
            await runtime.DisposeAsync();
    }
}
