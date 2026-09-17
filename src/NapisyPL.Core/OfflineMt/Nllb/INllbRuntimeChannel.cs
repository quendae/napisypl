using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.OfflineMt.Nllb;

public interface INllbRuntimeChannel : IAsyncDisposable
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
