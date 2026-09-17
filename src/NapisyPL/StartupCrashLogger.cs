using System.Text;

namespace NapisyPL;

internal static class StartupCrashLogger
{
    public static string LogPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SubFlow",
                "logs");
            return Path.Combine(directory, "startup-crash.log");
        }
    }

    public static void Write(string stage, Exception exception)
    {
        try
        {
            var path = LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var text = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("O"))
                .Append(" stage=").Append(stage)
                .Append(" process=").Append(Environment.ProcessId)
                .AppendLine()
                .AppendLine(exception.ToString())
                .AppendLine()
                .ToString();
            File.AppendAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // A crash logger must never replace the original startup failure.
        }
    }
}
