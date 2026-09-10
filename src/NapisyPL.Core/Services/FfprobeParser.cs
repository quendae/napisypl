using System.Text.Json;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Services;

public static class FfprobeParser
{
    private static readonly HashSet<string> TextCodecs = new(StringComparer.OrdinalIgnoreCase)
    {
        "subrip", "srt", "ass", "ssa", "webvtt", "mov_text", "text", "subviewer", "subviewer1",
        "microdvd", "jacosub", "mpl2", "pjs", "realtext", "sami"
    };

    public static IReadOnlyList<SubtitleTrack> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<SubtitleTrack>();
        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("index", out var indexElement) || !indexElement.TryGetInt32(out var index))
                continue;

            var codec = stream.TryGetProperty("codec_name", out var codecElement)
                ? codecElement.GetString() ?? "unknown"
                : "unknown";
            string? language = null;
            string? title = null;
            if (stream.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Object)
            {
                if (tags.TryGetProperty("language", out var languageElement)) language = languageElement.GetString();
                if (tags.TryGetProperty("title", out var titleElement)) title = titleElement.GetString();
            }

            result.Add(new SubtitleTrack(index, codec, language, title, TextCodecs.Contains(codec)));
        }

        return result;
    }

    public static SubtitleTrack? ChooseDefault(IReadOnlyList<SubtitleTrack> tracks) =>
        tracks.FirstOrDefault(t => t.IsText && IsEnglish(t.Language)) ?? tracks.FirstOrDefault(t => t.IsText);

    private static bool IsEnglish(string? language) =>
        language is not null && (language.Equals("eng", StringComparison.OrdinalIgnoreCase) || language.Equals("en", StringComparison.OrdinalIgnoreCase));
}
