using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL;

public partial class MainWindow
{
    internal static readonly FilePickerFileType PolishSubtitleFileType = new("Napisy")
    {
        Patterns = ["*.srt", "*.ass", "*.ssa", "*.vtt"]
    };

    private async void OnSubtitleSourcesClick(object? sender, RoutedEventArgs e)
    {
        var saved = await new Sources.SubtitleSourcesWindow().ShowDialog<bool?>(this);
        if (saved == true)
        {
            var count = _subtitlePipeline.OnlineSources.Count;
            SetStatus(count == 0
                ? "Źródła napisów wyłączone: szukamy tylko w QNapi."
                : $"Źródła napisów: {string.Join(", ", _subtitlePipeline.OnlineSources.Select(source => source.Name))}.",
                StatusKind.Normal);
        }
    }

    /// <summary>Polish subtitles from another release, re-timed to this video by its English subtitles.</summary>
    private async void OnSyncOtherReleaseClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || !StorageProvider.CanOpen)
            return;
        if (_inputPath is null || !TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(_inputPath)))
        {
            SetStatus("Najpierw wybierz film.", StatusKind.Error);
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz polskie napisy z innej wersji filmu",
            AllowMultiple = false,
            FileTypeFilter = [PolishSubtitleFileType, FilePickerFileTypes.All]
        });
        var polishPath = files.FirstOrDefault()?.Path.LocalPath;
        if (polishPath is null)
            return;

        var videoPath = _inputPath;
        ResetOperationProgress();
        BeginOperation("Dopasowuję polskie napisy do filmu…", indeterminate: true);
        try
        {
            var outcome = await _subtitlePipeline.SynchronizeOtherReleaseAsync(
                videoPath,
                polishPath,
                englishReference: null,
                TrackComboBox.SelectedItem as SubtitleTrack,
                new Progress<string>(message => SetStatus(message, StatusKind.Normal)),
                _operationCancellation!.Token);

            _appLogger.Info("other_release_sync",
                ("file", Path.GetFileName(videoPath)),
                ("decision", outcome.Decision),
                ("saved", outcome.Saved));
            if (outcome.Saved)
            {
                _lastOutputPath = outcome.OutputPath;
                ProgressBar.Value = 100;
                OpenFolderButton.Content = "Pokaż plik";
                OpenFolderButton.IsVisible = true;
            }
            SetStatus(outcome.Detail, outcome.Saved ? StatusKind.Success : StatusKind.Error);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Anulowano dopasowanie napisów.", StatusKind.Normal);
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyError(ex), StatusKind.Error);
        }
        finally
        {
            EndOperation(keepProgress: _lastOutputPath is not null);
            RefreshReadyState();
        }
    }
}
