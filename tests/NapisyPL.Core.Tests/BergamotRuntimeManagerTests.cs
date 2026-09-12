using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class BergamotRuntimeManagerTests
{
    [Fact]
    public async Task EnsureReadyAndTranslate_LoadsOnceAndReordersSegmentsById()
    {
        var channel = new FakeChannel();
        var options = new BergamotRuntimeOptions("fake-helper.exe", TimeSpan.FromSeconds(5));
        await using var manager = new BergamotRuntimeManager(
            options,
            "C:/models/firefox-en-pl",
            _ => channel);

        await manager.EnsureReadyAsync();
        await manager.EnsureReadyAsync();
        var result = await manager.TranslateAsync(["first", "second"]);

        Assert.Equal(1, channel.SentCommands.OfType<LoadCommand>().Count());
        Assert.Equal("C:/models/firefox-en-pl", channel.SentCommands.OfType<LoadCommand>().Single().ModelPath);
        Assert.Equal(new[] { "FIRST-PL", "SECOND-PL" }, result);
    }

    [Fact]
    public async Task TranslateAsync_WhenCompleteArrivesBeforeEverySegment_ThrowsInvalidDataException()
    {
        var channel = new FakeChannel(omitLastSegment: true);
        var options = new BergamotRuntimeOptions("fake-helper.exe", TimeSpan.FromSeconds(5));
        await using var manager = new BergamotRuntimeManager(
            options,
            "C:/models/firefox-en-pl",
            _ => channel);

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => manager.TranslateAsync(["first", "second"]));

        Assert.Contains("all translated segments", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DisposeAsync_StopsRuntimeChannel()
    {
        var channel = new FakeChannel();
        var options = new BergamotRuntimeOptions("fake-helper.exe", TimeSpan.FromSeconds(5));
        var manager = new BergamotRuntimeManager(
            options,
            "C:/models/firefox-en-pl",
            _ => channel);

        await manager.EnsureReadyAsync();
        await manager.DisposeAsync();

        Assert.True(channel.StopCalled);
    }

    private sealed class FakeChannel(bool omitLastSegment = false) : IBergamotRuntimeChannel
    {
        private readonly Queue<LocalTranslatorEvent> _events = new();

        public List<LocalTranslatorCommand> SentCommands { get; } = [];
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 1234;
        public bool StopCalled { get; private set; }

        public Task SendAsync(LocalTranslatorCommand command, CancellationToken cancellationToken = default)
        {
            SentCommands.Add(command);
            switch (command)
            {
                case LoadCommand:
                    _events.Enqueue(new ReadyEvent("fixture", "0.4.5"));
                    break;
                case TranslateCommand translate:
                    if (!omitLastSegment)
                    {
                        _events.Enqueue(new SegmentEvent(translate.JobId, 2, "SECOND-PL"));
                    }
                    _events.Enqueue(new SegmentEvent(translate.JobId, 1, "FIRST-PL"));
                    _events.Enqueue(new CompleteEvent(translate.JobId));
                    break;
            }
            return Task.CompletedTask;
        }

        public Task<LocalTranslatorEvent> ReadAsync(CancellationToken cancellationToken = default)
        {
            if (_events.Count == 0)
                throw new InvalidOperationException("Fake channel has no queued event.");
            return Task.FromResult(_events.Dequeue());
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            StopCalled = true;
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
