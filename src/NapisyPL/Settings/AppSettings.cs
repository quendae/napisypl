namespace NapisyPL.Settings;

public sealed class AppSettings
{
    public string Provider { get; set; } = "Local Argos (offline)";
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool ExportTxt { get; set; }
}
