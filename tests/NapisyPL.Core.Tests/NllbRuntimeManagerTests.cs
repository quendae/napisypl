using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.OfflineMt.Nllb;

namespace NapisyPL.Core.Tests;

public sealed class NllbRuntimeManagerTests
{
    [Fact]
    public async Task EnsureReadyAsync_LoadsModelOnce_AndTranslatePreservesOrder()
    {
        var channel = new FakeChannel();
        await using var runtime = new NllbRuntimeManager(
            new NllbRuntimeOptions("python.exe", "nllb_helper.py", TimeSpan.FromSeconds(5)),
            "C:\\models\\nllb",
            _ => channel);

        await runtime.EnsureReadyAsync();
        await runtime.EnsureReadyAsync();
        var translated = await runtime.TranslateAsync(["Hello", "Goodbye"]);

        Assert.Equal(["PL:Hello", "PL:Goodbye"], translated);
        var load = Assert.Single(channel.SentCommands.OfType<LoadCommand>());
        Assert.Equal("C:\\models\\nllb", load.ModelPath);
        Assert.Single(channel.SentCommands.OfType<TranslateCommand>());
        Assert.Equal(4242, runtime.ProcessId);
    }

    private sealed class FakeChannel : INllbRuntimeChannel
    {
        private readonly Queue<LocalTranslatorEvent> _events = new();
        public List<LocalTranslatorCommand> SentCommands { get; } = [];
        public bool IsRunning { get; private set; } = true;
        public int? ProcessId => 4242;

        public Task SendAsync(LocalTranslatorCommand command, CancellationToken cancellationToken = default)
        {
            SentCommands.Add(command);
            switch (command)
            {
                case LoadCommand:
                    _events.Enqueue(new ReadyEvent("fixture-nllb", "transformers-4.57.6"));
                    break;
                case TranslateCommand translate:
                    foreach (var segment in translate.Segments)
                        _events.Enqueue(new SegmentEvent(translate.JobId, segment.Id, "PL:" + segment.Text));
                    _events.Enqueue(new CompleteEvent(translate.JobId));
                    break;
            }
            return Task.CompletedTask;
        }

        public Task<LocalTranslatorEvent> ReadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(_events.Dequeue());

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
