using NapisyPL.Core.Security;

namespace NapisyPL.Core.Tests;

public sealed class ApiKeyStoreTests
{
    [Fact]
    public async Task SaveAndLoadAsync_StoresProviderSpecificEncryptedValueWithoutPlaintext()
    {
        var root = Path.Combine(Path.GetTempPath(), "SubFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "secrets.json");
            var store = new ApiKeyStore(path, new ReversingProtector());

            await store.SaveAsync("DeepL", "secret-deepl-key");
            await store.SaveAsync("Claude", "secret-claude-key");

            Assert.Equal("secret-deepl-key", await store.LoadAsync("DeepL"));
            Assert.Equal("secret-claude-key", await store.LoadAsync("Claude"));

            var raw = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("secret-deepl-key", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("secret-claude-key", raw, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task RemoveAsync_ForgetsOnlySelectedProvider()
    {
        var root = Path.Combine(Path.GetTempPath(), "SubFlowTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new ApiKeyStore(Path.Combine(root, "secrets.json"), new ReversingProtector());
            await store.SaveAsync("DeepL", "deepl");
            await store.SaveAsync("Gemini", "gemini");

            await store.RemoveAsync("DeepL");

            Assert.Null(await store.LoadAsync("DeepL"));
            Assert.Equal("gemini", await store.LoadAsync("Gemini"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plaintext) => plaintext.Reverse().ToArray();
        public byte[] Unprotect(byte[] ciphertext) => ciphertext.Reverse().ToArray();
    }
}
