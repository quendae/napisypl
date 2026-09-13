namespace NapisyPL.Core.OfflineMt.Nllb;

public static class NllbRuntimeRegistry
{
    private static readonly object Sync = new();
    private static readonly Dictionary<NllbModelProfile, NllbTranslatorClient> Clients = [];

    public static IOfflineMachineTranslatorClient GetOrCreate(HttpClient httpClient) =>
        GetOrCreate(httpClient, NllbModelProfile.Fast600M);

    public static IOfflineMachineTranslatorClient GetOrCreate(
        HttpClient httpClient,
        NllbModelProfile profile)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        lock (Sync)
        {
            if (Clients.TryGetValue(profile, out var existing))
                return existing;

            var paths = OfflineMtPaths.CreateDefault();
            var descriptor = NllbModelDescriptor.ForProfile(profile);
            var modelDirectory = paths.GetNllbProfileDirectory(profile);
            var assets = new OfflineMtAssetManager(modelDirectory);
            var runtime = new NllbRuntimeManager(CreateRuntimeOptions(), modelDirectory);
            var installer = new NllbAssetManager(httpClient, assets, descriptor);
            var client = new NllbTranslatorClient(
                assets,
                runtime,
                descriptor,
                DisplayName(profile),
                installer.InstallPinnedModelAsync);
            Clients[profile] = client;
            return client;
        }
    }

    public static string DisplayName(NllbModelProfile profile) => profile switch
    {
        NllbModelProfile.Fast600M => "NLLB 600M — Fast",
        NllbModelProfile.Balanced1_3B => "NLLB 1.3B — Balanced",
        NllbModelProfile.QualityMadlad3B => "MADLAD-400 3B — Quality",
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown offline MT model profile.")
    };

    private static NllbRuntimeOptions CreateRuntimeOptions()
    {
        var amdRuntimeDirectory = Path.Combine(AppContext.BaseDirectory, "nllb-amd-runtime");
        var amdPython = Path.Combine(amdRuntimeDirectory, "python.exe");
        var amdHelper = Path.Combine(amdRuntimeDirectory, "nllb_helper.py");
        if (File.Exists(amdPython) && File.Exists(amdHelper))
            return new NllbRuntimeOptions(amdPython, amdHelper, TimeSpan.FromMinutes(20));

        var helperExecutable = Path.Combine(
            AppContext.BaseDirectory,
            "nllb-runtime",
            "SubFlow.NllbHelper.exe");
        return new NllbRuntimeOptions(helperExecutable, string.Empty, TimeSpan.FromMinutes(20));
    }

    public static async ValueTask DisposeAsync()
    {
        NllbTranslatorClient[] clients;
        lock (Sync)
        {
            clients = Clients.Values.ToArray();
            Clients.Clear();
        }

        foreach (var client in clients)
            await client.DisposeAsync();
    }
}
