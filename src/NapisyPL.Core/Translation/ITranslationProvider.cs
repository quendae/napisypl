using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation;

public interface ITranslationProvider
{
    string DisplayName { get; }
    Task<IReadOnlyDictionary<int, string>> TranslateAsync(IReadOnlyList<TranslationSegment> segments, CancellationToken cancellationToken = default);
}
