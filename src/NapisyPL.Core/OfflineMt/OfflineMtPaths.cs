namespace NapisyPL.Core.OfflineMt;

public sealed record OfflineMtPaths(string RootDirectory)
{
    public string BergamotDirectory => Path.Combine(RootDirectory, "bergamot");
    public string OpusMarianDirectory => Path.Combine(RootDirectory, "opus-marian");
    public string Nllb600mDirectory => Path.Combine(RootDirectory, "nllb-600m");

    public static OfflineMtPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new OfflineMtPaths(Path.Combine(localAppData, "SubFlow", "offline-mt"));
    }
}
