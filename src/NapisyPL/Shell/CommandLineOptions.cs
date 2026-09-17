namespace NapisyPL.Shell;

public enum StartupAction
{
    /// <summary>Plain launch: show the main window.</summary>
    OpenMainWindow,
    /// <summary>Explorer context menu or tray: search subtitles for these paths.</summary>
    Search,
    /// <summary>Start in the tray only.</summary>
    Background,
    RegisterExplorerMenu,
    UnregisterExplorerMenu
}

public sealed record CommandLineOptions(StartupAction Action, IReadOnlyList<string> Paths)
{
    public const string SearchSwitch = "--search";
    public const string BackgroundSwitch = "--background";
    public const string RegisterSwitch = "--register-shell";
    public const string UnregisterSwitch = "--unregister-shell";

    public static CommandLineOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
            return new CommandLineOptions(StartupAction.OpenMainWindow, []);

        var first = args[0];
        if (first.Equals(RegisterSwitch, StringComparison.OrdinalIgnoreCase))
            return new CommandLineOptions(StartupAction.RegisterExplorerMenu, []);
        if (first.Equals(UnregisterSwitch, StringComparison.OrdinalIgnoreCase))
            return new CommandLineOptions(StartupAction.UnregisterExplorerMenu, []);
        if (first.Equals(BackgroundSwitch, StringComparison.OrdinalIgnoreCase))
            return new CommandLineOptions(StartupAction.Background, []);

        // "--search <path>..." from the context menu; a bare path (a file dropped on
        // the exe) means the same thing.
        var paths = (first.Equals(SearchSwitch, StringComparison.OrdinalIgnoreCase) ? args.Skip(1) : args)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim().Trim('"'))
            .ToArray();

        return paths.Length == 0
            ? new CommandLineOptions(StartupAction.OpenMainWindow, [])
            : new CommandLineOptions(StartupAction.Search, paths);
    }

    public string[] ToArguments() => Action switch
    {
        StartupAction.Search => [SearchSwitch, .. Paths],
        StartupAction.Background => [BackgroundSwitch],
        StartupAction.RegisterExplorerMenu => [RegisterSwitch],
        StartupAction.UnregisterExplorerMenu => [UnregisterSwitch],
        _ => []
    };
}
