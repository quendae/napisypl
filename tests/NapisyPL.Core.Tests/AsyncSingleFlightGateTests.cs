using NapisyPL.Core.ContextResolution;

namespace NapisyPL.Core.Tests;

public sealed class AsyncSingleFlightGateTests
{
    [Fact]
    public async Task RunAsync_ConcurrentWaiters_InvokeOperationOnce()
    {
        var gate = new AsyncSingleFlightGate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task Operation(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        var first = gate.RunAsync(Operation, CancellationToken.None, CancellationToken.None);
        await started.Task;
        var second = gate.RunAsync(Operation, CancellationToken.None, CancellationToken.None);

        await Task.Delay(25);
        Assert.Equal(1, Volatile.Read(ref calls));

        release.TrySetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task RunAsync_CancelingOneWaiter_DoesNotCancelSharedOperation()
    {
        var gate = new AsyncSingleFlightGate();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        async Task Operation(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        using var waiterCancellation = new CancellationTokenSource();
        var first = gate.RunAsync(Operation, CancellationToken.None, waiterCancellation.Token);
        await started.Task;
        var second = gate.RunAsync(Operation, CancellationToken.None, CancellationToken.None);

        waiterCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, Volatile.Read(ref calls));

        release.TrySetResult();
        await second;
        Assert.Equal(1, Volatile.Read(ref calls));
    }
}
