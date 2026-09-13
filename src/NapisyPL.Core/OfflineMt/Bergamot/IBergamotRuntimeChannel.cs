using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public interface IBergamotRuntimeChannel : IAsyncDisposable
{
    bool IsRunning { get; }
    int? ProcessId { get; }

    Task SendAsync(
        LocalTranslatorCommand command,
        CancellationToken cancellationToken = default);

    Task<LocalTranslatorEvent> ReadAsync(
        CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);
}
