using ZstdSharp;

namespace NapisyPL.Core.OfflineMt.Bergamot;

public static class FirefoxBergamotCompression
{
    public static byte[] DecompressZstd(byte[] compressed)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        using var decompressor = new Decompressor();
        return decompressor.Unwrap(compressed).ToArray();
    }
}
