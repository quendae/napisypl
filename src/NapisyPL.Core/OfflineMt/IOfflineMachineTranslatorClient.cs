namespace NapisyPL.Core.OfflineMt;

public interface IOfflineMachineTranslatorClient
{
    string BackendId { get; }
    string DisplayName { get; }

    Task EnsureReadyAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    OfflineMachineTranslatorInfo GetInfo();
}
