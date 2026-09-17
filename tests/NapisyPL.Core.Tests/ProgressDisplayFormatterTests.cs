using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL.Core.Tests;

public sealed class ProgressDisplayFormatterTests
{
    [Fact]
    public void Format_ShowsBatchSegmentAndElapsedWhileProviderWorks()
    {
        var started = new DateTimeOffset(2026, 9, 10, 19, 0, 0, TimeSpan.Zero);
        var progress = new TranslationProgress(40, 200, 3, 10, true, started);

        var text = ProgressDisplayFormatter.Format(progress, started.AddSeconds(7));

        Assert.Equal("Segment 40 / 200 · Partia 3 / 10 · 00:07", text);
    }

    [Fact]
    public void Format_DoesNotKeepElapsedClockAfterBatchCompletes()
    {
        var started = new DateTimeOffset(2026, 9, 10, 19, 0, 0, TimeSpan.Zero);
        var progress = new TranslationProgress(60, 200, 3, 10, false, started);

        var text = ProgressDisplayFormatter.Format(progress, started.AddMinutes(3));

        Assert.Equal("Segment 60 / 200 · Partia 3 / 10", text);
    }
}
