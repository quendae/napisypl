using Avalonia.Controls;
using Avalonia.Interactivity;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Subtitles.Online;

namespace NapisyPL.Sources;

/// <summary>Options → Źródła napisów: the user's own SubDL and OpenSubtitles.com keys.</summary>
public partial class SubtitleSourcesWindow : Window
{
    private readonly AppServices _services = AppServices.Shared;

    public SubtitleSourcesWindow()
    {
        InitializeComponent();
        Shell.DarkTitleBar.Apply(this);
        Opened += async (_, _) =>
        {
            SubDlKeyBox.Text = await _services.ApiKeys.LoadAsync(SubDlSource.SourceName) ?? string.Empty;
            OpenSubtitlesKeyBox.Text = await _services.ApiKeys.LoadAsync(OpenSubtitlesComSource.SourceName) ?? string.Empty;
        };
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            await SaveKeyAsync(SubDlSource.SourceName, SubDlKeyBox.Text);
            await SaveKeyAsync(OpenSubtitlesComSource.SourceName, OpenSubtitlesKeyBox.Text);
            await _services.RefreshOnlineSourcesAsync();
            Close(true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            StatusText.Text = "Nie udało się zapisać kluczy: " + exception.Message;
        }
    }

    private async Task SaveKeyAsync(string name, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            await _services.ApiKeys.RemoveAsync(name);
        else
            await _services.ApiKeys.SaveAsync(name, key.Trim());
    }

    /// <summary>Runs one real search per filled key, so a typo shows up here and not mid-batch.</summary>
    private async void OnTestClick(object? sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        StatusText.Text = "Sprawdzam…";
        try
        {
            await TestAsync(SubDlKeyBox.Text, SubDlStatus, key => new SubDlSource(_services.HttpClient, key));
            await TestAsync(OpenSubtitlesKeyBox.Text, OpenSubtitlesStatus,
                key => new OpenSubtitlesComSource(_services.HttpClient, key, AppServices.UserAgent));
            StatusText.Text = "Sprawdzanie zakończone. Wyszukiwanie nie zużywa limitu pobrań.";
        }
        finally
        {
            TestButton.IsEnabled = true;
            SaveButton.IsEnabled = true;
        }
    }

    private static async Task TestAsync(string? key, TextBlock status, Func<string, IOnlineSubtitleSource> create)
    {
        status.IsVisible = true;
        if (string.IsNullOrWhiteSpace(key))
        {
            status.Text = "Wyłączone — brak klucza.";
            return;
        }

        var query = new OnlineSubtitleQuery("Breaking.Bad.S01E01.720p.BluRay.x264.mkv",
            ReleaseName.Parse("Breaking.Bad.S01E01.720p.BluRay.x264.mkv"), MovieHash: null);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var found = await create(key.Trim()).SearchAsync(query, SubtitleLanguage.Polish, timeout.Token);
            status.Text = $"Klucz działa. Testowe wyszukiwanie znalazło {found.Count} polskich napisów.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException)
        {
            status.Text = exception is OperationCanceledException
                ? "Brak odpowiedzi w 30 sekund. Sprawdź połączenie z internetem."
                : exception.Message;
        }
    }

    private void OnRevealChanged(object? sender, RoutedEventArgs e)
    {
        var reveal = RevealCheckBox.IsChecked == true;
        SubDlKeyBox.RevealPassword = reveal;
        OpenSubtitlesKeyBox.RevealPassword = reveal;
    }

    private void OnGetSubDlKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://subdl.com/panel/api");

    private void OnGetOpenSubtitlesKeyClick(object? sender, RoutedEventArgs e) => OpenUrl("https://www.opensubtitles.com/consumers");

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private static void OpenUrl(string url) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
}
