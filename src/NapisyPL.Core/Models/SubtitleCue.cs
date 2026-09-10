namespace NapisyPL.Core.Models;

public sealed record SubtitleCue(int Index, TimeSpan Start, TimeSpan End, string Text);
