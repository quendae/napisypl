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

    public static byte[] DecompressZstd(byte[] compressed, long expectedDecompressedSize)
    {
        ArgumentNullException.ThrowIfNull(compressed);
        if (expectedDecompressedSize < 0 || expectedDecompressedSize > int.MaxValue)
            throw new InvalidDataException("Firefox model decompressed size is outside the supported range.");

        try
        {
            using var decompressor = new Decompressor();
            var result = new byte[(int)expectedDecompressedSize];
            var written = decompressor.Unwrap(compressed.AsSpan(), result.AsSpan());
            if (written != result.Length)
                throw new InvalidDataException("Firefox model decompressed size does not match Remote Settings metadata.");
            return result;
        }
        catch (ZstdException ex)
        {
            throw new InvalidDataException("Firefox model Zstd payload could not be decompressed within the declared size.", ex);
        }
    }
}
