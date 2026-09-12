using System.Text;
using NapisyPL.Core.OfflineMt.Bergamot;

namespace NapisyPL.Core.Tests;

public sealed class FirefoxBergamotCompressionTests
{
    [Fact]
    public void DecompressZstd_DecodesKnownFrame()
    {
        var compressed = Convert.FromBase64String(
            "KLUv/SQiEQEAU3ViRmxvdyBGaXJlZm94IEJlcmdhbW90IHpzdGQgdGVzdAj6KbQ=");

        var decompressed = FirefoxBergamotCompression.DecompressZstd(compressed);

        Assert.Equal("SubFlow Firefox Bergamot zstd test", Encoding.UTF8.GetString(decompressed));
    }
}
