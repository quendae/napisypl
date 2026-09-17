using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace NapisyPL.Shell;

/// <summary>
/// One SubFlow per user. Selecting ten files in Explorer and choosing the context
/// menu starts ten processes; the first one owns the tray and the others hand their
/// paths over a named pipe and exit.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string Identity = "SubFlow." + Environment.UserName;
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _stop = new();

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, @"Local\" + Identity, out var createdNew);
        if (createdNew)
            return new SingleInstance(mutex);

        mutex.Dispose();
        return null;
    }

    /// <summary>Sends the arguments to the running instance. Retries while it is still starting.</summary>
    public static bool TryForward(IReadOnlyList<string> arguments, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(arguments));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", Identity, PipeDirection.Out);
                client.Connect(500);
                client.Write(payload);
                client.Flush();
                return true;
            }
            catch (TimeoutException)
            {
            }
            catch (IOException)
            {
                Thread.Sleep(150);
            }
        }

        return false;
    }

    /// <summary>Receives arguments from later launches until disposed.</summary>
    public void Listen(Action<string[]> onArguments)
    {
        ArgumentNullException.ThrowIfNull(onArguments);
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Identity, PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_stop.Token);
                    using var buffer = new MemoryStream();
                    await server.CopyToAsync(buffer, _stop.Token);
                    var arguments = JsonSerializer.Deserialize<string[]>(buffer.ToArray());
                    if (arguments is not null)
                        onArguments(arguments);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception exception) when (exception is IOException or JsonException)
                {
                    // A client that disconnected mid-message; keep listening.
                }
            }
        });
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _mutex.ReleaseMutex(); } catch (ApplicationException) { }
        _mutex.Dispose();
        _stop.Dispose();
    }
}
