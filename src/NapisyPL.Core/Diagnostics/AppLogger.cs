using System.Globalization;
using System.Text;

namespace NapisyPL.Core.Diagnostics;

public sealed class AppLogger : IAppLogger, IDisposable
{
    private static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "provider", "model", "file", "segmentCount", "characterCount", "batch", "batchCount",
        "elapsedMs", "httpStatus", "category", "version", "result", "reason", "reasonCode", "stage",
        "action", "sourceWordCount", "outputWordCount", "initialIssueCount", "retryCount", "remainingIssueCount", "fallbackCount",
        "fileIndex", "fileCount", "completed", "skipped", "failed", "candidateCount",
        "windowIndex", "windowCount", "cueCount", "promptChars", "promptTokens", "completionTokens",
        "responseChars", "speakerCount", "uniqueSpeakerCount", "knownGenderCount", "genderEvidenceCount", "knownGenderEvidenceCount", "topK",
        "knownCueGenderCount", "localTurnResolvedCount", "resolvedAddresseeCount",
        "directionalCueGenderCount", "hardVoiceResolvedCount", "hardVoiceChangedCount", "hardVoiceMode", "reviewMode",
        "knownSpeakerCandidateCount", "knownAddresseeCandidateCount", "knownSpeakerCandidateIds", "knownAddresseeCandidateIds",
        "eligibleSpeakerCandidateCount", "eligibleSpeakerCandidateIds", "speakerCandidateEvidence",
        "genderSampleCount", "maleTagSampleCount", "femaleTagSampleCount", "anyGenderTagSampleCount",
        "maleScoreMaxPermille", "femaleScoreMaxPermille", "combinedScoreMeanPermille", "combinedScoreMaxPermille",
        "speaker", "sampleCount", "voiceGender", "maleMeanPermille", "femaleMeanPermille",
        "combinedMeanPermille", "normalizedWinnerPermille", "winnerCount", "oppositeCount", "requiredWinnerCount",
        "dropped", "finishReason", "proposed", "backend", "device",
        "dropOutsideBatch", "dropMissingSpeaker", "dropContextGuard", "dropApplyGuard",
        "dropApplyCueMismatch", "dropApplyConfidence", "dropApplyFragment", "dropApplyUnchanged",
        "dropApplyInflection", "dropApplyFindMissing", "dropApplyFindAmbiguous",
        "cue", "resolver", "targetGender", "confidencePermille", "gate", "candidate", "matchedWord", "replacement", "changed",
        "speakerGender", "speakerConfidencePermille", "speakerSampleCount",
        "cueGender", "cueDirectionalGender", "cueDirectionalConfidencePermille", "forcedCueGender", "forcedCueConfidencePermille",
        "nextCue", "nextSpeaker", "nextCueGender", "nextCueConfidencePermille", "nextCueCombinedPermille", "nextCueDurationMs",
        "nextCueDirectionalGender", "nextCueDirectionalConfidencePermille", "nextForcedCueGender", "nextForcedCueConfidencePermille",
        "nextSpeakerGender", "nextSpeakerConfidencePermille", "nextSpeakerSampleCount"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    private const int FlushThresholdChars = 64 * 1024;

    private readonly object _writeGate = new();
    private readonly StringBuilder _pending = new();
    private DateTime _lastFlushUtc = DateTime.UtcNow;

    // Without this the last lines of a run stay in memory until the next log
    // event or until the window closes, which hides the review summary exactly
    // when someone copies the log to inspect a finished run.
    private readonly Timer _flushTimer;

    public AppLogger(string? logDirectory = null)
    {
        var directory = logDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NapisyPL",
            "logs");

        Directory.CreateDirectory(directory);
        LogPath = Path.Combine(directory, $"napisypl-{DateTime.Now:yyyy-MM-dd}.log");
        _flushTimer = new Timer(_ => Flush(), null, FlushInterval, FlushInterval);
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
        {
            _pending.Append(line);
            // Opening and closing the file per line put a synchronous round trip
            // inside the per-cue analysis loops. Batch instead, and flush on a
            // short interval so a crash still leaves recent context on disk.
            if (_pending.Length >= FlushThresholdChars ||
                DateTime.UtcNow - _lastFlushUtc >= FlushInterval)
            {
                FlushLocked();
            }
        }
    }

    /// <summary>Writes anything still buffered. Safe to call repeatedly.</summary>
    public void Flush()
    {
        lock (_writeGate)
            FlushLocked();
    }

    private void FlushLocked()
    {
        if (_pending.Length == 0)
            return;

        try
        {
            File.AppendAllText(LogPath, _pending.ToString(), Utf8NoBom);
        }
        catch (IOException)
        {
            // Diagnostics must never take the translation down with them.
        }
        catch (UnauthorizedAccessException)
        {
        }

        _pending.Clear();
        _lastFlushUtc = DateTime.UtcNow;
    }

    public void Dispose()
    {
        _flushTimer.Dispose();
        Flush();
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
