using System.Text;
using NapisyPL.Core.Security;

namespace NapisyPL.Core.Tests;

public sealed class WindowsDpapiSecretProtectorTests
{
    [Fact]
    public void ProtectAndUnprotect_RoundTripsForCurrentWindowsUser()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var protector = new WindowsDpapiSecretProtector();
        var plaintext = Encoding.UTF8.GetBytes("subflow-secret-ąę-test");

        var cipher = protector.Protect(plaintext);
        var restored = protector.Unprotect(cipher);

        Assert.NotEqual(Convert.ToBase64String(plaintext), Convert.ToBase64String(cipher));
        Assert.Equal(plaintext, restored);
    }
}
