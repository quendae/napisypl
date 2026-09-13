namespace NapisyPL.Core.LocalTranslation;

public interface IArgosTranslatorClient
{
    Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
