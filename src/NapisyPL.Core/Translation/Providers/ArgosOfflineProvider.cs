using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class ArgosOfflineProvider : ITranslationProvider
{
    private readonly IArgosTranslatorClient _client;
    private readonly MachineTranslationTextPreprocessor _preprocessor;

    public ArgosOfflineProvider(IArgosTranslatorClient client)
        : this(client, new MachineTranslationTextPreprocessor())
    {
    }

    public ArgosOfflineProvider(
        IArgosTranslatorClient client,
        MachineTranslationTextPreprocessor preprocessor)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _preprocessor = preprocessor ?? throw new ArgumentNullException(nameof(preprocessor));
    }

    public string DisplayName => "Argos EN→PL";
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
