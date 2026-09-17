using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public sealed class DeferredTranslationProvider : ITranslationProvider
{
    private readonly Lazy<ITranslationProvider> _inner;

    public DeferredTranslationProvider(Func<ITranslationProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _inner = new Lazy<ITranslationProvider>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private ITranslationProvider Inner => _inner.Value;

    public string DisplayName => Inner.DisplayName;

    public TranslationBatchPolicy BatchPolicy => Inner.BatchPolicy;

    public Task<IReadOnlyDictionary<int, string>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        CancellationToken cancellationToken = default) =>
        Inner.TranslateAsync(segments, cancellationToken);
}
