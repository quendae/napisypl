namespace NapisyPL.Core.OfflineMt.Nllb;

public sealed record NllbRuntimeOptions(
    string PythonPath,
    string HelperPath,
    TimeSpan StartupTimeout);
