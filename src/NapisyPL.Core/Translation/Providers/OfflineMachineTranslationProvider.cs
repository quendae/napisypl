using NapisyPL.Core.Models;
using NapisyPL.Core.OfflineMt;

namespace NapisyPL.Core.Translation.Providers;

public sealed class OfflineMachineTranslationProvider : ITranslationProvider
{
    private readonly IOfflineMachineTranslatorClient _client;
    private readonly MachineTranslationTextPreprocessor _preprocessor;

    public OfflineMachineTranslationProvider(IOfflineMachineTranslatorClient client)
        : this(client, new MachineTranslationTextPreprocessor())
    {
    }

    public OfflineMachineTranslationProvider(
        IOfflineMachineTranslatorClient client,
        MachineTranslationTextPreprocessor preprocessor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
    }

    public string DisplayName => _client.DisplayName;
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.MachineTranslation;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return new Dictionary<int, string>();

        var batch = _preprocessor.Prepare(segments);
        var translatedParts = batch.Parts.Count == 0
            ? Array.Empty<string>()
            : (await _client.TranslateAsync(batch.Parts, cancellationToken)).ToArray();

        return _preprocessor.Reassemble(batch, translatedParts);
    }
}
