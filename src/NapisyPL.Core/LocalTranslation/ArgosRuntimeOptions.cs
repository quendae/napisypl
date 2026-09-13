namespace NapisyPL.Core.LocalTranslation;

public sealed record ArgosRuntimeOptions(
    string BaseDirectory,
    string ModelUrl,
    string ModelFileName)
{
    public string ModelDirectory => Path.Combine(BaseDirectory, "models");
    public string PackagesDirectory => Path.Combine(BaseDirectory, "packages");
    public string ModelPath => Path.Combine(ModelDirectory, ModelFileName);
    public string HelperPath => Path.Combine(AppContext.BaseDirectory, "SubFlow.ArgosHelper.exe");

    public static ArgosRuntimeOptions CreateDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new ArgosRuntimeOptions(
            Path.Combine(localAppData, "SubFlow", "argos"),
            "https://data.argosopentech.com/argospm/v1/translate-en_pl-1_9.argosmodel",
            "translate-en_pl-1_9.argosmodel");
    }
}
