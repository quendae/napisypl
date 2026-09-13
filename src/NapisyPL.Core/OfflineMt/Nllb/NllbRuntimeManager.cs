using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.OfflineMt.Nllb;

public sealed class NllbRuntimeManager : IAsyncDisposable
{
    private readonly NllbRuntimeOptions _options;
    private readonly string _modelDirectory;
    private readonly Func<NllbRuntimeOptions, INllbRuntimeChannel> _channelFactory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private INllbRuntimeChannel? _channel;
    private bool _loaded;
    private bool _disposed;

    public NllbRuntimeManager(
        NllbRuntimeOptions options,
        string modelDirectory,
        Func<NllbRuntimeOptions, INllbRuntimeChannel>? channelFactory = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _modelDirectory = modelDirectory ?? throw new ArgumentNullException(nameof(modelDirectory));
        _channelFactory = channelFactory ?? (runtimeOptions => new NllbProcessRuntimeChannel(runtimeOptions));
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
            var channel = _channel ?? throw new InvalidOperationException("NLLB helper is not running.");
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
                            throw new InvalidDataException($"NLLB helper returned unexpected segment id {segment.Id}.");
                        result[segment.Id - 1] = segment.Text;
                        break;
                    case LocalProgressEvent progress when progress.JobId == jobId:
                        StatusProgress?.Report($"NLLB: tłumaczenie lokalne {progress.Completed} / {progress.Total}…");
                        break;
                    case CompleteEvent complete when complete.JobId == jobId:
                        if (result.Any(text => text is null))
                            throw new InvalidDataException("NLLB helper completed before returning all translated segments.");
                        return result.Select(text => text!).ToArray();
                    case LocalErrorEvent error when error.JobId is null || error.JobId == jobId:
                        throw new InvalidOperationException($"NLLB helper: {error.Code}: {error.Message}");
                }
            }
        }
        catch
        {
            if (_channel is { IsRunning: false })
                await ResetChannelCoreAsync();
            throw;
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

        if (_channel is not null)
            await ResetChannelCoreAsync();

        status?.Report("NLLB: uruchamiam lokalny translator…");
        var channel = _channelFactory(_options);
        _channel = channel;
        _loaded = false;

        try
        {
            await channel.SendAsync(new LoadCommand(_modelDirectory), cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.StartupTimeout);

            while (true)
            {
                var @event = await channel.ReadAsync(timeout.Token);
                switch (@event)
                {
                    case ReadyEvent:
                        _loaded = true;
                        status?.Report("NLLB: lokalny translator EN→PL gotowy.");
                        return;
                    case LocalErrorEvent error:
                        throw new InvalidOperationException($"NLLB helper: {error.Code}: {error.Message}");
                }
            }
        }
        catch
        {
            await ResetChannelCoreAsync();
            throw;
        }
    }

    private async Task ResetChannelCoreAsync()
    {
        var channel = _channel;
        _channel = null;
        _loaded = false;
        if (channel is null)
            return;

        try { await channel.StopAsync(); } catch { }
        await channel.DisposeAsync();
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await _gate.WaitAsync();
        try
        {
            if (_disposed)
                return;
            await ResetChannelCoreAsync();
            _disposed = true;
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
