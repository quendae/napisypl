using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.OfflineMt.Nllb;

if (args.Length != 1)
    throw new ArgumentException("Usage: NllbSmoke <SubFlow.NllbHelper.exe>");

var helperPath = Path.GetFullPath(args[0]);
if (!File.Exists(helperPath))
    throw new FileNotFoundException("Packaged NLLB helper was not found.", helperPath);

var root = Path.Combine(Path.GetTempPath(), "subflow-nllb-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    var assets = new OfflineMtAssetManager(root);
    var installer = new NllbAssetManager(httpClient, assets);
    await using var runtime = new NllbRuntimeManager(
        new NllbRuntimeOptions(helperPath, string.Empty, TimeSpan.FromMinutes(10)),
        root);
    await using var client = new NllbTranslatorClient(
        assets,
        runtime,
        installer.InstallPinnedModelAsync);

    var status = new Progress<string>(message => Console.Error.WriteLine(message));
    await client.EnsureReadyAsync(status);

    var source = "Hello, how are you?";
    var translated = await client.TranslateAsync([source]);
    if (translated.Count != 1 || string.IsNullOrWhiteSpace(translated[0]))
        throw new InvalidDataException("NLLB did not return the single smoke translation.");
    if (string.Equals(translated[0].Trim(), source, StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("NLLB smoke translation is identical to the English source.");

    var lower = translated[0].ToLowerInvariant();
    var polishMarkers = new[] { "jak", "się", "masz", "cześć", "witaj" };
    if (!polishMarkers.Any(lower.Contains))
        throw new InvalidDataException($"NLLB smoke translation does not look Polish: {translated[0]}");

    Console.WriteLine($"NLLB smoke translation: {translated[0]}");

    var batchSources = Enumerable.Range(0, 66)
        .Select(index => (index % 3) switch
        {
            0 => "Hello, how are you?",
            1 => "Thank you very much.",
            _ => "See you tomorrow."
        })
        .ToArray();
    var batch = await client.TranslateAsync(batchSources);
    if (batch.Count != batchSources.Length)
        throw new InvalidDataException($"NLLB returned {batch.Count} translations for {batchSources.Length} inputs.");
    if (batch.Any(string.IsNullOrWhiteSpace))
        throw new InvalidDataException("NLLB returned an empty translation in the 66-segment batch.");
    if (batch.Zip(batchSources).Any(pair => string.Equals(pair.First.Trim(), pair.Second.Trim(), StringComparison.OrdinalIgnoreCase)))
        throw new InvalidDataException("NLLB returned an unchanged English source in the 66-segment batch.");

    var info = client.GetInfo();
    if (!info.BenchmarkOnly || !string.Equals(info.LicenseId, "CC-BY-NC-4.0", StringComparison.Ordinal))
        throw new InvalidDataException("NLLB benchmark/license metadata is incorrect.");

    Console.WriteLine($"NLLB batch smoke: {batch.Count} segments OK");
    Console.WriteLine("NLLB_SMOKE_OK");
}
finally
{
    try { Directory.Delete(root, recursive: true); } catch { }
}
