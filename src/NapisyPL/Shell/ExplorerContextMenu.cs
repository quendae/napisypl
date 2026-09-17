using System.Runtime.Versioning;
using Microsoft.Win32;
using NapisyPL.Core.Services;

namespace NapisyPL.Shell;

/// <summary>
/// "Szukaj napisów z SubFlow" on folders and video files, registered for the current
/// user only (HKCU), so it needs no administrator rights. The installer calls the
/// same code through <c>--register-shell</c> and <c>--unregister-shell</c>.
/// On Windows 11 classic entries sit under "Pokaż więcej opcji".
/// </summary>
[SupportedOSPlatform("windows")]
public static class ExplorerContextMenu
{
    public const string MenuText = "Szukaj napisów z SubFlow";
    private const string VerbName = "SubFlow.Search";
    private const string ClassesRoot = @"Software\Classes";

    private static IEnumerable<string> VerbParents()
    {
        yield return @"Directory\shell";
        foreach (var extension in TranslationPipeline.VideoExtensions.Order(StringComparer.OrdinalIgnoreCase))
            yield return $@"SystemFileAssociations\{extension}\shell";
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesRoot}\Directory\shell\{VerbName}\command");
        return key?.GetValue(null) is string command &&
               command.Contains(ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    public static void Register()
    {
        var command = $"\"{ExecutablePath}\" {CommandLineOptions.SearchSwitch} \"%1\"";
        foreach (var parent in VerbParents())
        {
            using var verb = Registry.CurrentUser.CreateSubKey($@"{ClassesRoot}\{parent}\{VerbName}");
            verb.SetValue(null, MenuText);
            verb.SetValue("Icon", $"\"{ExecutablePath}\",0");
            // Explorer starts one process per selected item; SingleInstance merges them.
            verb.SetValue("MultiSelectModel", "Player");
            using var commandKey = verb.CreateSubKey("command");
            commandKey.SetValue(null, command);
        }
    }

    public static void Unregister()
    {
        foreach (var parent in VerbParents())
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesRoot}\{parent}\{VerbName}", throwOnMissingSubKey: false);
    }

    private static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "NapisyPL.exe");
}
