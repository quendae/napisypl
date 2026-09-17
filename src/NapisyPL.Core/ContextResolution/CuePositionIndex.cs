using System.Runtime.CompilerServices;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.ContextResolution;

/// <summary>
/// Maps cue id to list position. Every resolver used to linear-scan the cue list
/// to find one index, once per cue, which made each review pass quadratic.
/// The index is cached per cue-list instance so callers keep their existing
/// signatures and still pay the lookup only once.
/// </summary>
internal static class CuePositionIndex
{
    private static readonly ConditionalWeakTable<object, Dictionary<int, int>> Cache = new();

    public static int Find(IReadOnlyList<SubtitleCue> cues, int cueId)
    {
        var map = Cache.GetValue(cues, static key => Build((IReadOnlyList<SubtitleCue>)key));
        return map.TryGetValue(cueId, out var position) ? position : -1;
    }

    private static Dictionary<int, int> Build(IReadOnlyList<SubtitleCue> cues)
    {
        var map = new Dictionary<int, int>(cues.Count);
        for (var index = 0; index < cues.Count; index++)
            map[cues[index].Index] = index;
        return map;
    }
}
