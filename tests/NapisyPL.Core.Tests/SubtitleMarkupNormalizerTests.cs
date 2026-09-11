using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class SubtitleMarkupNormalizerTests
{
    [Theory]
    [InlineData("Cześć. < i > Habla < / i > English.", "Cześć. <i>Habla</i> English.")]
    [InlineData("< B >ważne< / B >", "<b>ważne</b>")]
    [InlineData("bez tagów", "bez tagów")]
    public void Normalize_RepairsSimpleSubtitleTags(string input, string expected)
    {
        Assert.Equal(expected, SubtitleMarkupNormalizer.Normalize(input));
    }
}
