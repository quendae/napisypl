namespace NapisyPL.Core.ContextResolution;

public interface IContextResolver
{
    Task<ContextMap> ResolveAsync(
        IReadOnlyList<ContextCue> cues,
        CancellationToken cancellationToken = default);
}
