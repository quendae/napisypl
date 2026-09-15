using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Tests;

public sealed class DeferredTranslationProviderTests
{
    [Fact]
    public void Construction_DoesNotCreateInnerProvider()
    {
        var calls = 0;

        _ = new DeferredTranslationProvider(() =>
        {
            calls++;
            return new FakeProvider();
        });

        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task MembersAndTranslation_CreateInnerProviderOnlyOnce()
    {
        var calls = 0;
        var provider = new DeferredTranslationProvider(() =>
        {
            calls++;
            return new FakeProvider();
        });

        Assert.Equal("fake", provider.DisplayName);
        Assert.Equal(TranslationBatchPolicy.LlmDefault, provider.BatchPolicy);
        var result = await provider.TranslateAsync([new TranslationSegment(1, "Hello")]);

        Assert.Equal("PL: Hello", Assert.Single(result).Value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void FactoryFailure_IsDeferredUntilFirstMemberAccess()
    {
        var provider = new DeferredTranslationProvider(() => throw new InvalidOperationException("factory failed"));

        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            _ = provider.DisplayName;
        });

        Assert.Equal("factory failed", error.Message);
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        public string DisplayName => "fake";
        public TranslationBatchPolicy BatchPolicy => TranslationBatchPolicy.LlmDefault;

        public Task<IReadOnlyDictionary<int, string>> TranslateAsync(
            IReadOnlyList<TranslationSegment> segments,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(
                segments.ToDictionary(segment => segment.Id, segment => "PL: " + segment.Text));
    }
}
