using NapisyPL.Core.Models;
using NapisyPL.Core.OfflineMt;
using NapisyPL.Core.Translation.Providers;

namespace NapisyPL.Core.Tests;

public sealed class OfflineMachineTranslationProviderTests
{
    [Fact]
    public async Task TranslateAsync_UsesSameVisualWrapReconstructionAsArgos()
    {
        var client = new RecordingClient(texts => texts.Select(text => text switch
        {
            "Do you think they're ever gonna forget today?" => "Myślisz, że kiedykolwiek zapomną ten dzień?",
            "Never." => "Nigdy.",
            _ => throw new InvalidOperationException(text)
        }).ToArray());
        var provider = new OfflineMachineTranslationProvider(client);

        var result = await provider.TranslateAsync([
            new TranslationSegment(253, "Do you think they're ever gonna\nforget today? Never.")
        ]);

        Assert.Equal(
            ["Do you think they're ever gonna forget today?", "Never."],
            client.LastTexts);
        Assert.Equal("Myślisz, że kiedykolwiek zapomną ten dzień? Nigdy.", result[253]);
        Assert.Equal("Fixture Offline MT", provider.DisplayName);
        Assert.Equal(40, provider.BatchPolicy.MaxSegments);
    }

    [Fact]
    public async Task TranslateAsync_KeepsTaggedDialogueTurnsSeparate()
    {
        var client = new RecordingClient(texts => texts.Select(text => text switch
        {
            "<i>- Yeah, sí, problema.</i>" => "<i>- Tak, sí, problema.</i>",
            "<i>- And now dos problemas.</i>" => "<i>- A teraz dos problemas.</i>",
            _ => throw new InvalidOperationException(text)
        }).ToArray());
        var provider = new OfflineMachineTranslationProvider(client);

        var result = await provider.TranslateAsync([
            new TranslationSegment(30, "<i>- Yeah, sí, problema.</i>\n<i>- And now dos problemas.</i>")
        ]);

        Assert.Equal(
            ["<i>- Yeah, sí, problema.</i>", "<i>- And now dos problemas.</i>"],
            client.LastTexts);
        Assert.Equal(
            "<i>- Tak, sí, problema.</i>\n<i>- A teraz dos problemas.</i>",
            result[30]);
    }

    [Fact]
    public async Task TranslateAsync_RejectsMismatchedResultCount()
    {
        var provider = new OfflineMachineTranslationProvider(
            new RecordingClient(_ => ["Tylko jeden"]));

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.TranslateAsync([
            new TranslationSegment(1, "One"),
            new TranslationSegment(2, "Two")
        ]));
    }

    private sealed class RecordingClient(
        Func<IReadOnlyList<string>, IReadOnlyList<string>> translate) : IOfflineMachineTranslatorClient
    {
        public string BackendId => "fixture-offline-mt";
        public string DisplayName => "Fixture Offline MT";
        public IReadOnlyList<string> LastTexts { get; private set; } = [];

        public Task EnsureReadyAsync(
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> TranslateAsync(
            IReadOnlyList<string> texts,
            CancellationToken cancellationToken = default)
        {
            LastTexts = texts.ToArray();
            return Task.FromResult(translate(texts));
        }

        public OfflineMachineTranslatorInfo GetInfo() => new(
            BackendId,
            DisplayName,
            "fixture-model",
            "1",
            "fixture",
            new string('0', 64),
            1,
            "fixture-runtime",
            "1",
            "CPU",
            "MIT",
            true);
    }
}
