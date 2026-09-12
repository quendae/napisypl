using Avalonia;

namespace NapisyPL;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
                StartupCrashLogger.Write("appdomain_unhandled", exception);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            StartupCrashLogger.Write("unobserved_task", eventArgs.Exception);

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            StartupCrashLogger.Write("main", exception);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
