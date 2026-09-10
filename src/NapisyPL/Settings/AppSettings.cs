namespace NapisyPL.Settings;

public sealed class AppSettings
{
    public string Provider { get; set; } = "Gemini";
    public string Model { get; set; } = "gemini-3.8-flash";
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
    public bool ExportTxt { get; set; }
}
