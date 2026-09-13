namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed record BergamotRuntimeOptions(
    string HelperPath,
    TimeSpan LoadTimeout)
{
    public BergamotRuntimeOptions(string helperPath)
        : this(helperPath, TimeSpan.FromMinutes(2))
    {
    }
}
