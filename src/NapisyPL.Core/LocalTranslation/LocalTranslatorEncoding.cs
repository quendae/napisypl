using System.Text;

namespace NapisyPL.Core.LocalTranslation;

public static class LocalTranslatorEncoding
{
    public static Encoding Utf8NoBom { get; } = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}
