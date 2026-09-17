using Avalonia;
using NapisyPL.Shell;

namespace NapisyPL;

internal static class Program
{
    public static CommandLineOptions StartupOptions { get; private set; } = new(StartupAction.OpenMainWindow, []);

    /// <summary>Held by the first SubFlow of this user; null in a process that only forwarded its arguments.</summary>
    public static SingleInstance? Instance { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                StartupCrashLogger.Write("appdomain_unhandled", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            StartupCrashLogger.Write("unobserved_task", eventArgs.Exception);

        StartupOptions = CommandLineOptions.Parse(args);

        // The installer calls these; they must not start the UI.
        if (StartupOptions.Action is StartupAction.RegisterExplorerMenu or StartupAction.UnregisterExplorerMenu)
            return ChangeExplorerMenu(StartupOptions.Action);

        Instance = SingleInstance.TryAcquire();
        if (Instance is null)
        {
            // Another SubFlow owns the tray: hand it the request ("--search …" or a plain
            // launch, which shows its main window) and leave.
            return SingleInstance.TryForward(StartupOptions.ToArguments(), TimeSpan.FromSeconds(10)) ? 0 : 1;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return 0;
        }
        catch (Exception exception)
        {
            StartupCrashLogger.Write("main", exception);
            throw;
        }
    }

    private static int ChangeExplorerMenu(StartupAction action)
    {
        if (!OperatingSystem.IsWindows())
            return 1;
        try
        {
            if (action == StartupAction.RegisterExplorerMenu)
                ExplorerContextMenu.Register();
            else
                ExplorerContextMenu.Unregister();
            return 0;
        }
        catch (Exception exception)
        {
            StartupCrashLogger.Write("explorer_menu", exception);
            return 1;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
