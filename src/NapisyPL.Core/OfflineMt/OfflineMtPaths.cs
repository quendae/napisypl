using NapisyPL.Core.OfflineMt.Nllb;

namespace NapisyPL.Core.OfflineMt;

public sealed record OfflineMtPaths(string RootDirectory)
{
    public string BergamotDirectory => Path.Combine(RootDirectory, "bergamot");
    public string OpusMarianDirectory => Path.Combine(RootDirectory, "opus-marian");
    public string Nllb600mDirectory => Path.Combine(RootDirectory, "nllb-600m");
    public string Nllb1_3bDirectory => Path.Combine(RootDirectory, "nllb-1.3b");
    public string Nllb3_3bDirectory => Path.Combine(RootDirectory, "nllb-3.3b");
    public string Madlad3bDirectory => Path.Combine(RootDirectory, "madlad-400-3b");

    public string GetNllbProfileDirectory(NllbModelProfile profile) => profile switch
    {
        NllbModelProfile.Fast600M => Nllb600mDirectory,
        NllbModelProfile.Balanced1_3B => Nllb1_3bDirectory,
        NllbModelProfile.QualityNllb3_3B => Nllb3_3bDirectory,
        NllbModelProfile.QualityMadlad3B => Madlad3bDirectory,
        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown offline MT model profile.")
    };

    public static OfflineMtPaths CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new OfflineMtPaths(Path.Combine(localAppData, "SubFlow", "offline-mt"));
    }
}
