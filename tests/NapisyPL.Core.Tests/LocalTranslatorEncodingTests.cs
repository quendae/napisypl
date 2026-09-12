using System.Text;
using NapisyPL.Core.LocalTranslation;

namespace NapisyPL.Core.Tests;

public sealed class LocalTranslatorEncodingTests
{
    [Fact]
    public void ProtocolUtf8_DoesNotEmitBomBeforeFirstJsonCommand()
    {
        Assert.Empty(LocalTranslatorEncoding.Utf8NoBom.GetPreamble());

        using var stream = new MemoryStream();
        using (var writer = new StreamWriter(stream, LocalTranslatorEncoding.Utf8NoBom, leaveOpen: true))
        {
            writer.WriteLine("{\"type\":\"load\"}");
            writer.Flush();
        }

        var bytes = stream.ToArray();
        Assert.NotEmpty(bytes);
        Assert.Equal((byte)'{', bytes[0]);
        Assert.Equal("{\"type\":\"load\"}\n", Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));
    }
}
