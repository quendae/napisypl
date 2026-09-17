namespace NapisyPL.Core.Subtitles.Online;

/// <summary>
/// The OpenSubtitles hash: file size plus the 64-bit little-endian sums of the first and
/// last 64 KiB. Subtitles found by it were timed for exactly this file.
/// </summary>
public static class MovieHash
{
    private const int ChunkSize = 64 * 1024;

    public static async Task<string?> ComputeAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, ChunkSize, useAsync: true);
            if (stream.Length < ChunkSize)
                return null;

            var hash = (ulong)stream.Length;
            var buffer = new byte[ChunkSize];
            hash = unchecked(hash + await SumAsync(stream, 0, buffer, cancellationToken));
            hash = unchecked(hash + await SumAsync(stream, stream.Length - ChunkSize, buffer, cancellationToken));
            return hash.ToString("x16");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ulong Sum(ReadOnlySpan<byte> chunk)
    {
        ulong sum = 0;
        for (var offset = 0; offset + 8 <= chunk.Length; offset += 8)
            sum = unchecked(sum + BitConverter.ToUInt64(chunk.Slice(offset, 8)));
        return sum;
    }

    private static async Task<ulong> SumAsync(Stream stream, long position, byte[] buffer, CancellationToken cancellationToken)
    {
        stream.Position = position;
        await stream.ReadExactlyAsync(buffer, cancellationToken);
        return Sum(buffer);
    }
}
