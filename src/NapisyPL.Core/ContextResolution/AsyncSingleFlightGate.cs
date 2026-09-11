namespace NapisyPL.Core.ContextResolution;

public sealed class AsyncSingleFlightGate
{
    private readonly object _sync = new();
    private Task? _activeTask;

    public Task RunAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken operationCancellationToken,
        CancellationToken waiterCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        Task activeTask;
        lock (_sync)
        {
            if (_activeTask is null || _activeTask.IsCompleted)
                _activeTask = RunOperationAsync(operation, operationCancellationToken);
            activeTask = _activeTask;
        }

        return waiterCancellationToken.CanBeCanceled
            ? activeTask.WaitAsync(waiterCancellationToken)
            : activeTask;
    }

    private async Task RunOperationAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        await operation(cancellationToken).ConfigureAwait(false);
    }
}
