using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using NapisyPL.Core.OfflineMt.Nllb;
using NapisyPL.Search;
using NapisyPL.Shell;

namespace NapisyPL;

/// <summary>
/// SubFlow lives in the tray. Closing the main window hides it; "Zamknij" in the tray
/// menu quits. Context-menu launches open the search window without the main window.
/// </summary>
public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private SearchWindow? _searchWindow;
    private TrayIcon? _tray;
    private bool _quitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public bool IsMainWindowVisible => _mainWindow?.IsVisible == true;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => OnExit();
            CreateTray();

            Program.Instance?.Listen(arguments =>
                Dispatcher.UIThread.Post(() => Handle(CommandLineOptions.Parse(arguments))));
            Handle(Program.StartupOptions);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Handle(CommandLineOptions options)
    {
        switch (options.Action)
        {
            case StartupAction.Search:
                ShowSearchWindow().AddPaths(options.Paths);
                break;
            case StartupAction.Background:
                break;
            default:
                ShowMainWindow();
                break;
        }
    }

    private void CreateTray()
    {
        var searchFile = new NativeMenuItem("Szukaj napisów dla pliku…");
        searchFile.Click += async (_, _) => await ShowSearchWindow().PickFileAsync();
        var searchFolder = new NativeMenuItem("Wskaż folder…");
        searchFolder.Click += async (_, _) => await ShowSearchWindow().PickFolderAsync();
        var open = new NativeMenuItem("Otwórz SubFlow");
        open.Click += (_, _) => ShowMainWindow();
        var quit = new NativeMenuItem("Zamknij");
        quit.Click += (_, _) => Quit();

        _tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NapisyPL/Assets/subflow.ico"))),
            ToolTipText = "SubFlow — napisy po polsku",
            Menu = new NativeMenu { Items = { searchFile, searchFolder, new NativeMenuItemSeparator(), open, quit } }
        };
        _tray.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, [_tray]);
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closing += (_, e) =>
            {
                if (_quitting)
                    return;
                // Hide to the tray; the GPU model is released unless a job still needs it.
                e.Cancel = true;
                _mainWindow.Hide();
                ReleaseModelWhenIdle();
            };
        }

        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private SearchWindow ShowSearchWindow()
    {
        if (_searchWindow is null)
        {
            _searchWindow = new SearchWindow();
            _searchWindow.Closed += (_, _) => _searchWindow = null;
        }

        _searchWindow.Show();
        if (_searchWindow.WindowState == WindowState.Minimized)
            _searchWindow.WindowState = WindowState.Normal;
        _searchWindow.Activate();
        return _searchWindow;
    }

    private async void ReleaseModelWhenIdle()
    {
        if (_searchWindow is not null || _mainWindow?.IsBusy == true || AppServices.Shared.TranslationGate.CurrentCount == 0)
            return;
        await NllbRuntimeRegistry.DisposeAsync();
    }

    private void Quit()
    {
        _quitting = true;
        _mainWindow?.Close();
        _searchWindow?.Close();
        _desktop?.Shutdown();
    }

    private void OnExit()
    {
        _tray?.Dispose();
        AppServices.Shared.Dispose();
        Program.Instance?.Dispose();
    }
}
