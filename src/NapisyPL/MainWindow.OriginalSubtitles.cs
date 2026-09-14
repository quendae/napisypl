using Avalonia.Interactivity;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;

namespace NapisyPL;

public partial class MainWindow
{
    private async void OnExtractOriginalSubtitleClick(object? sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        if (_inputPath is null ||
            !TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(_inputPath)))
        {
            SetStatus("Najpierw wybierz plik wideo z osadzonymi napisami.", StatusKind.Error);
            return;
        }

        if (TrackComboBox.SelectedItem is not SubtitleTrack { IsText: true } track)
        {
            SetStatus("Wybierz tekstową ścieżkę napisów do wyciągnięcia.", StatusKind.Error);
            return;
        }

        ResetOperationProgress();
        ExtractOriginalSubtitleButton.IsEnabled = false;
        BeginOperation("Wyciągam oryginalne napisy…", indeterminate: true);

        try
        {
            var outputPath = OriginalSubtitleExportPath.Build(_inputPath);
            var extraction = new SubtitleExtractionService(
                new FfmpegManager(_httpClient),
                new ProcessRunner());
            var statusProgress = new Progress<string>(message => SetStatus(message, StatusKind.Normal));

            await extraction.ExtractToSrtAsync(
                _inputPath,
                track,
                outputPath,
                statusProgress,
                _operationCancellation!.Token);

            _lastOutputPath = outputPath;
            ProgressBar.Value = 100;
            SetStatus(
                $"Gotowe — wyciągnięto oryginalne napisy. Zapisano {Path.GetFileName(outputPath)}",
                StatusKind.Success);
            OpenFolderButton.Content = "Pokaż plik";
            OpenFolderButton.IsVisible = true;
        }
        catch (OperationCanceledException)
        {
            SetStatus("Anulowano wyciąganie oryginalnych napisów.", StatusKind.Normal);
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyError(ex), StatusKind.Error);
        }
        finally
        {
            EndOperation(keepProgress: _lastOutputPath is not null);
            ExtractOriginalSubtitleButton.IsEnabled =
                _inputPath is not null &&
                TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(_inputPath)) &&
                TrackComboBox.SelectedItem is SubtitleTrack { IsText: true };
            RefreshReadyState();
        }
    }
}
