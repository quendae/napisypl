using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Models;

namespace NapisyPL.Core.Tests;

public sealed class LocalTranslatorProtocolTests
{
    [Fact]
    public void SerializeCommand_WritesCompactTranslateRecord()
    {
        var line = LocalTranslatorProtocol.SerializeCommand(
            new TranslateCommand("job-1", [new TranslationSegment(7, "Hello") ]));

        Assert.Contains("\"type\":\"translate\"", line);
        Assert.Contains("\"jobId\":\"job-1\"", line);
        Assert.Contains("\"id\":7", line);
        Assert.Contains("\"text\":\"Hello\"", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void ParseEvent_ParsesSegmentAndProgressEvents()
    {
        var segment = Assert.IsType<SegmentEvent>(LocalTranslatorProtocol.ParseEvent(
            "{\"type\":\"segment\",\"jobId\":\"job-1\",\"id\":7,\"text\":\"Cześć\"}"));
        Assert.Equal("job-1", segment.JobId);
        Assert.Equal(7, segment.Id);
        Assert.Equal("Cześć", segment.Text);

        var progress = Assert.IsType<LocalProgressEvent>(LocalTranslatorProtocol.ParseEvent(
            "{\"type\":\"progress\",\"jobId\":\"job-1\",\"completed\":2,\"total\":5}"));
        Assert.Equal(2, progress.Completed);
        Assert.Equal(5, progress.Total);
    }

    [Fact]
    public void ParseEvent_RejectsUnknownOrIncompleteRecords()
    {
        Assert.Throws<InvalidDataException>(() => LocalTranslatorProtocol.ParseEvent("{\"type\":\"mystery\"}"));
        Assert.Throws<InvalidDataException>(() => LocalTranslatorProtocol.ParseEvent("{\"type\":\"segment\",\"id\":1,\"text\":\"x\"}"));
        Assert.Throws<InvalidDataException>(() => LocalTranslatorProtocol.ParseEvent("not json"));
    }

    [Fact]
    public void SerializeCommand_RejectsInvalidJobId()
    {
        Assert.Throws<ArgumentException>(() =>
            LocalTranslatorProtocol.SerializeCommand(new TranslateCommand("", [new TranslationSegment(1, "Hello")])));
    }
}
