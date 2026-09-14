using System.Text.Json;
using NapisyPL.Settings;

namespace NapisyPL.Core.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void ExportTxt_IsSessionOnly_AndIsNotPersisted()
    {
        var settings = new AppSettings { ExportTxt = true };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("exportTxt", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExportTxt_FromOlderSettings_IsIgnored()
    {
        var settings = JsonSerializer.Deserialize<AppSettings>("{\"exportTxt\":true}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(settings);
        Assert.False(settings!.ExportTxt);
    }
}
