using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Translation.Providers;

public sealed class ArgosOfflineProvider(IArgosTranslatorClient client) : ITranslationProvider
{
    public string DisplayName => "Argos EN→PL";
    public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.MachineTranslation;

    public async Task<IReadOnlyDictionary<int, string>> TranslateAsync(
        IReadOnlyList<TranslationSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments.Count == 0)
            return new Dictionary<int, string>();

        var translated = await client.TranslateAsync(
            segments.Select(segment => segment.Text).ToArray(),
            cancellationToken);

        if (translated.Count != segments.Count)
            throw new InvalidDataException("Argos zwrócił inną liczbę segmentów niż wysłano.");

        var result = new Dictionary<int, string>(segments.Count);
        for (var i = 0; i < segments.Count; i++)
            result[segments[i].Id] = translated[i];
        return result;
    }
}
