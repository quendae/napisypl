using Avalonia.Interactivity;

namespace NapisyPL;

public partial class MainWindow
{
    private async void OnSearchSubtitlesChanged(object? sender, RoutedEventArgs e)
    {
        // XAML raises the event before the constructor has composed the services.
        if (_subtitlePipeline is null)
            return;
        _subtitlePipeline.Enabled = SearchSubtitlesCheckBox.IsChecked == true;
        _folderBatch.AllowMissingSubtitleTracks = _subtitlePipeline.Enabled;
        RefreshReadyState();
        await SaveSettingsAsync();
    }

    private void ReportSubtitleStatus(string message)
    {
        SetStatus(message, StatusKind.Normal);
        _appLogger.Info("subtitle_status", ("message", message));
    }
}
