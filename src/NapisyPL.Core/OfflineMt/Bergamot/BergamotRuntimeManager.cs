using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public sealed class BergamotRuntimeManager : IAsyncDisposable
{
    private readonly BergamotRuntimeOptions _options;
    private readonly string _modelDirectory;
    private readonly Func<BergamotRuntimeOptions, IBergamotRuntimeChannel> _channelFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IBergamotRuntimeChannel? _channel;
    private bool _loaded;
    private bool _disposed;

    public BergamotRuntimeManager(
        BergamotRuntimeOptions options,
        string modelDirectory,
        Func<BergamotRuntimeOptions, IBergamotRuntimeChannel>? channelFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(modelDirectory))
            throw new ArgumentException("Bergamot model directory cannot be empty.", nameof(modelDirectory));
        if (_options.LoadTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Bergamot load timeout must be positive.");

        _modelDirectory = modelDirectory;
        _channelFactory = channelFactory ?? (runtimeOptions => new BergamotProcessRuntimeChannel(runtimeOptions));
    }

    public bool IsRunning => _channel?.IsRunning == true;
    public int? ProcessId => _channel?.ProcessId;
    public IProgress<string>? StatusProgress { get; set; }

    public async Task EnsureReadyAsync(
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureReadyCoreAsync(status ?? StatusProgress, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ThrowIfDisposed();
        if (texts.Count == 0)
            return [];

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureReadyCoreAsync(StatusProgress, cancellationToken);
            var channel = _channel ?? throw new InvalidOperationException("Bergamot helper is not running.");
            var jobId = Guid.NewGuid().ToString("N");
            var segments = texts.Select((text, index) => new TranslationSegment(index + 1, text)).ToArray();
            await channel.SendAsync(new TranslateCommand(jobId, segments), cancellationToken);

            var result = new string?[texts.Count];
            while (true)
            {
                var @event = await channel.ReadAsync(cancellationToken);
                switch (@event)
                {
                    case SegmentEvent segment when segment.JobId == jobId:
                        if (segment.Id <= 0 || segment.Id > result.Length)
                            throw new InvalidDataException($"Bergamot helper returned unexpected segment id {segment.Id}.");
                        result[segment.Id - 1] = segment.Text;
                        break;

                    case LocalProgressEvent progress when progress.JobId == jobId:
                        StatusProgress?.Report($"Bergamot: local translation {progress.Completed} / {progress.Total}…");
                        break;

                    case CompleteEvent complete when complete.JobId == jobId:
                        if (result.Any(text => text is null))
                            throw new InvalidDataException("Bergamot helper completed before returning all translated segments.");
                        return result.Select(text => text!).ToArray();

                    case LocalErrorEvent error when error.JobId is null || error.JobId == jobId:
                        throw new InvalidOperationException($"Bergamot helper: {error.Code}: {error.Message}");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task EnsureReadyCoreAsync(
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        if (_loaded && _channel?.IsRunning == true)
            return;

        await StopChannelCoreAsync();
        status?.Report("Bergamot: starting local translation runtime…");
        var channel = _channelFactory(_options)
            ?? throw new InvalidOperationException("Bergamot runtime channel factory returned null.");
        _channel = channel;
        _loaded = false;

        try
        {
            await channel.SendAsync(new LoadCommand(_modelDirectory), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.LoadTimeout);

            while (true)
            {
                var @event = await channel.ReadAsync(timeout.Token);
                switch (@event)
                {
                    case ReadyEvent:
                        _loaded = true;
                        status?.Report("Bergamot: local EN→PL translator ready.");
                        return;
                    case LocalErrorEvent error:
                        throw new InvalidOperationException($"Bergamot helper: {error.Code}: {error.Message}");
                }
            }
        }
        catch
        {
            await StopChannelCoreAsync();
            throw;
        }
    }

    private async Task StopChannelCoreAsync()
    {
        var channel = _channel;
        _channel = null;
        _loaded = false;
        if (channel is null)
            return;

        try
        {
            await channel.StopAsync(CancellationToken.None);
        }
        finally
        {
            await channel.DisposeAsync();
        }
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync();
        try
        {
            await StopChannelCoreAsync();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
