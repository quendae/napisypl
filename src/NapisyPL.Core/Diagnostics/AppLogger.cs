using System.Globalization;
using System.Text;

namespace NapisyPL.Core.Diagnostics;

public sealed class AppLogger : IAppLogger
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider", "model", "file", "segmentCount", "characterCount", "batch", "batchCount",
        "elapsedMs", "httpStatus", "category", "version", "result", "reason", "reasonCode", "stage",
        "fileIndex", "fileCount", "completed", "skipped", "failed", "candidateCount",
        "windowIndex", "windowCount", "cueCount", "promptChars", "promptTokens", "completionTokens",
        "responseChars", "speakerCount", "knownGenderCount", "genderEvidenceCount", "knownGenderEvidenceCount", "topK",
        "knownSpeakerCandidateCount", "knownAddresseeCandidateCount", "knownSpeakerCandidateIds", "knownAddresseeCandidateIds",
        "genderSampleCount", "maleTagSampleCount", "femaleTagSampleCount", "anyGenderTagSampleCount",
        "maleScoreMaxPermille", "femaleScoreMaxPermille", "combinedScoreMeanPermille", "combinedScoreMaxPermille",
        "speaker", "sampleCount", "voiceGender", "maleMeanPermille", "femaleMeanPermille",
        "combinedMeanPermille", "normalizedWinnerPermille", "winnerCount", "oppositeCount", "requiredWinnerCount",
        "dropped", "finishReason", "proposed", "backend", "device",
        "dropOutsideBatch", "dropMissingSpeaker", "dropContextGuard", "dropApplyGuard",
        "dropApplyCueMismatch", "dropApplyConfidence", "dropApplyFragment", "dropApplyUnchanged",
        "dropApplyInflection", "dropApplyFindMissing", "dropApplyFindAmbiguous"
    };

    private readonly object _writeGate = new();

    public AppLogger(string? logDirectory = null)
    {
        var directory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NapisyPL",
            "logs");

        Directory.CreateDirectory(directory);
        LogPath = Path.Combine(directory, $"napisypl-{DateTime.Now:yyyy-MM-dd}.log");
    }

    public string LogPath { get; }

    public void Info(string eventName, params (string Key, object? Value)[] fields) =>
        Write("INFO", eventName, fields);

    public void Error(string eventName, params (string Key, object? Value)[] fields) =>
        Write("ERROR", eventName, fields);

    private void Write(string level, string eventName, IReadOnlyList<(string Key, object? Value)> fields)
    {
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture))
            .Append(" level=").Append(level)
            .Append(" event=").Append(Sanitize(eventName));

        foreach (var (key, value) in fields)
        {
            line.Append(' ').Append(SanitizeKey(key)).Append('=');
            line.Append(AllowedFields.Contains(key) ? Sanitize(FormatValue(value)) : "[REDACTED]");
        }

        line.AppendLine();
        lock (_writeGate)
            File.AppendAllText(LogPath, line.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => value.ToString() ?? string.Empty
    };

    private static string Sanitize(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal)
             .Replace("\t", " ", StringComparison.Ordinal);

    private static string SanitizeKey(string key) =>
        string.IsNullOrWhiteSpace(key) ? "field" : Sanitize(key).Replace(' ', '_');
}
