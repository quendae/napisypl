using System.Text.Json.Serialization;

namespace NapisyPL.Settings;

public sealed class AppSettings
{
    public const string DefaultProvider = "MADLAD-400 3B — Quality";

    public string Provider { get; set; } = DefaultProvider;
    public string Model { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public bool SearchSubtitles { get; set; } = true;

    /// <summary>Fix gendered forms (zrobiłeś → zrobiłaś) from the voices in the video.</summary>
    public bool GenderCorrection { get; set; } = true;

    /// <summary>Search the legacy opensubtitles.org API, which needs no key while it lasts.</summary>
    public bool LegacyOpenSubtitles { get; set; } = true;

    /// <summary>Only change a form when the voice evidence is clear.</summary>
    public bool StrictVoiceEvidence { get; set; } = true;

    // Output format is intentionally session-only. Persisting this flag made TXT
    // unexpectedly re-enable on a later launch even when the user did not select it.
    [JsonIgnore]
    public bool ExportTxt { get; set; }
}
