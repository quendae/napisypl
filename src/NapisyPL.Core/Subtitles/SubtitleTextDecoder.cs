using System.Text;

namespace NapisyPL.Core.Subtitles;

/// <summary>
/// Polish subtitles from the web are often Windows-1250 or ISO-8859-2, not UTF-8.
/// A byte-order mark wins; valid UTF-8 is UTF-8; otherwise the Polish letters decide.
/// </summary>
public static class SubtitleTextDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static SubtitleTextDecoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
        }

        var windows1250 = Encoding.GetEncoding(1250).GetString(bytes);
        var iso88592 = Encoding.GetEncoding(28592).GetString(bytes);
        return PolishLetterCount(iso88592) > PolishLetterCount(windows1250) ? iso88592 : windows1250;
    }

    public static async Task<string> ReadFileAsync(string path, CancellationToken cancellationToken = default) =>
        Decode(await File.ReadAllBytesAsync(path, cancellationToken));

    // ś, ź and Ś, Ź sit on different bytes in the two code pages; the right one yields real letters.
    private static int PolishLetterCount(string text) =>
        text.Count(character => "ąćęłńóśźżĄĆĘŁŃÓŚŹŻ".Contains(character));
}
