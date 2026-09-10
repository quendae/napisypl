namespace NapisyPL.Core.ContextResolution;

public sealed class ContextResolutionCoordinator(int windowSize = 40, int overlap = 10)
{
    private readonly int _windowSize = windowSize > 0 ? windowSize : throw new ArgumentOutOfRangeException(nameof(windowSize));
    private readonly int _overlap = overlap >= 0 && overlap < windowSize ? overlap : throw new ArgumentOutOfRangeException(nameof(overlap));

    public async Task<ContextMap> ResolveAsync(
        IReadOnlyList<ContextCue> cues,
        IContextResolver resolver,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (cues.Count == 0)
            return new ContextMap(new Dictionary<string, SpeakerContext>(), new Dictionary<int, LineContext>());

        var step = _windowSize - _overlap;
        var windows = new List<IReadOnlyList<ContextCue>>();
        for (var start = 0; start < cues.Count; start += step)
        {
            windows.Add(cues.Skip(start).Take(_windowSize).ToArray());
            if (start + _windowSize >= cues.Count)
                break;
        }

        var speakers = new Dictionary<string, SpeakerContext>(StringComparer.Ordinal);
        var lines = new Dictionary<int, LineContext>();

        for (var i = 0; i < windows.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var partial = await resolver.ResolveAsync(windows[i], cancellationToken);

            foreach (var (speakerId, candidate) in partial.Speakers)
            {
                if (!speakers.TryGetValue(speakerId, out var current) || candidate.Confidence > current.Confidence)
                    speakers[speakerId] = candidate;
            }

            foreach (var (lineId, candidate) in partial.Lines)
            {
                if (!lines.TryGetValue(lineId, out var current) || candidate.Confidence > current.Confidence)
                    lines[lineId] = candidate;
            }

            progress?.Report((double)(i + 1) / windows.Count);
        }

        return new ContextMap(speakers, lines);
    }
}
