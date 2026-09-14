using System.Text.Json.Serialization;

namespace NapisyPL.Settings;

public sealed class AppSettings
{
    public string Provider { get; set; } = "Local Argos (offline)";
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    // Output format is intentionally session-only. Persisting this flag made TXT
    // unexpectedly re-enable on a later launch even when the user did not select it.
    [JsonIgnore]
    public bool ExportTxt { get; set; }
}
