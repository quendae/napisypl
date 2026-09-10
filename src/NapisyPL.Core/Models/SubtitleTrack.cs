namespace NapisyPL.Core.Models;

public sealed record SubtitleTrack(int StreamIndex, string Codec, string? Language, string? Title, bool IsText)
{
    public string DisplayName => $"{(Language ?? "?").ToUpperInvariant()} · {Codec}{(string.IsNullOrWhiteSpace(Title) ? string.Empty : $" · {Title}")}";
}
