using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Bergamot;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Smoke <SubFlow.BergamotHelper.exe>");
    return 2;
}

var helperPath = Path.GetFullPath(args[0]);
if (!File.Exists(helperPath))
{
    Console.Error.WriteLine($"Helper not found: {helperPath}");
    return 2;
}

var root = Path.Combine(Path.GetTempPath(), "subflow-offline-mt-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
var progress = new Progress<string>(message => Console.WriteLine($"STATUS {message}"));
var failures = new List<string>();

try
{
    await RunSmokeAsync("Firefox/Bergamot", SmokeFirefoxAsync);
    await RunSmokeAsync("OPUS-MT/Marian", SmokeOpusAsync);

    if (failures.Count != 0)
    {
        Console.Error.WriteLine("OFFLINE_MT_SMOKE_FAILED");
        foreach (var failure in failures)
            Console.Error.WriteLine(failure);
        return 1;
    }

    Console.WriteLine("OFFLINE_MT_SMOKE_OK");
    return 0;
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}

async Task RunSmokeAsync(string provider, Func<Task> smoke)
{
    try
    {
        await smoke();
        Console.WriteLine($"SMOKE {provider} PASS");
    }
    catch (Exception ex)
    {
        var failure = $"SMOKE {provider} FAIL: {ex.GetType().Name}: {ex.Message}";
        failures.Add(failure);
        Console.Error.WriteLine(failure);
        Console.Error.WriteLine(ex);
    }
}

async Task SmokeFirefoxAsync()
{
    var directory = Path.Combine(root, "firefox");
    var assets = new OfflineMtAssetManager(directory);
    var runtime = new BergamotRuntimeManager(new BergamotRuntimeOptions(helperPath, TimeSpan.FromMinutes(3)), directory);
    await using var client = new FirefoxBergamotTranslatorClient(httpClient, assets, runtime);

    Console.WriteLine("SMOKE Firefox/Bergamot start");
    await client.EnsureReadyAsync(progress);
    DumpConfigIfPresent(directory, "Firefox/Bergamot");
    var output = await client.TranslateAsync(["Hello, how are you?"]);
    AssertTranslation("Firefox/Bergamot", output);
}

async Task SmokeOpusAsync()
{
    var directory = Path.Combine(root, "opus");
    var assets = new OfflineMtAssetManager(directory);
    var runtime = new BergamotRuntimeManager(new BergamotRuntimeOptions(helperPath, TimeSpan.FromMinutes(3)), directory);
    await using var client = new OpusMarianTranslatorClient(httpClient, assets, runtime);

    Console.WriteLine("SMOKE OPUS-MT/Marian start");
    await client.EnsureReadyAsync(progress);
    DumpConfigIfPresent(directory, "OPUS-MT/Marian");
    var output = await client.TranslateAsync(["Hello, how are you?"]);
    AssertTranslation("OPUS-MT/Marian", output);
}

static void DumpConfigIfPresent(string directory, string provider)
{
    var configPath = Path.Combine(directory, "config.yml");
    if (!File.Exists(configPath))
        return;

    Console.WriteLine($"SMOKE {provider} config.yml:");
    Console.WriteLine(File.ReadAllText(configPath));
}

static void AssertTranslation(string provider, IReadOnlyList<string> output)
{
    if (output.Count != 1 || string.IsNullOrWhiteSpace(output[0]))
        throw new InvalidOperationException($"{provider} did not return one non-empty translation.");

    Console.WriteLine($"SMOKE {provider} result: {output[0]}");
}
