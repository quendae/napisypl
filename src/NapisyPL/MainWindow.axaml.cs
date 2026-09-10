using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;
using NapisyPL.Settings;

namespace NapisyPL;

public partial class MainWindow : Window
{
    private static readonly string[] ProviderNames = ["Gemini", "DeepL", "OpenAI / Ollama", "Claude"];

    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly AppLogger _appLogger = new();
    private readonly FolderQueuePlanner _folderQueuePlanner = new();
    private readonly MediaProbeService _mediaProbe;
    private readonly TranslationPipeline _pipeline;
    private readonly FolderBatchService _folderBatch;
    private readonly SettingsStore _settingsStore = new();
    private readonly DispatcherTimer _elapsedTimer;

    private CancellationTokenSource? _operationCancellation;
    private IReadOnlyList<SubtitleTrack> _tracks = [];
    private string? _inputPath;
    private string? _inputFolder;
    private string? _lastOutputPath;
    private string? _lastOutputFolder;
    private int _folderItemCount;
    private bool _busy;
    private bool _loadingSettings = true;
    private TranslationProgress? _activeTranslationProgress;

    private readonly IBrush _normalDropBrush = new SolidColorBrush(Color.Parse("#D8DDE5"));
    private readonly IBrush _activeDropBrush = new SolidColorBrush(Color.Parse("#202A3A"));
    private readonly IBrush _mutedBrush = new SolidColorBrush(Color.Parse("#667085"));
    private readonly IBrush _errorBrush = new SolidColorBrush(Color.Parse("#B42318"));
    private readonly IBrush _successBrush = new SolidColorBrush(Color.Parse("#18794E"));

    public MainWindow()
    {
        InitializeComponent();

        var processRunner = new ProcessRunner();
        var ffmpegManager = new FfmpegManager(_httpClient);
        _mediaProbe = new MediaProbeService(ffmpegManager, processRunner);
        var extraction = new SubtitleExtractionService(ffmpegManager, processRunner);
        var parser = new SrtParser();
        var writer = new SubtitleWriter();
        var coordinator = new TranslationCoordinator();
        _pipeline = new TranslationPipeline(parser, writer, extraction, coordinator);
        _folderBatch = new FolderBatchService(_folderQueuePlanner, _mediaProbe, _pipeline, _appLogger);

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => RefreshElapsedText();

        ProviderComboBox.ItemsSource = ProviderNames;
        Opened += OnOpened;
        Closed += (_, _) =>
        {
            _elapsedTimer.Stop();
            _operationCancellation?.Cancel();
            _operationCancellation?.Dispose();
            _httpClient.Dispose();
        };
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        var settings = await _settingsStore.LoadAsync();
        ProviderComboBox.SelectedItem = ProviderNames.Contains(settings.Provider) ? settings.Provider : "Gemini";
        ModelTextBox.Text = settings.Model;
        BaseUrlTextBox.Text = settings.BaseUrl;
        ExportTxtCheckBox.IsChecked = settings.ExportTxt;
        _loadingSettings = false;
        ApplyProviderUi(useDefaults: false);
        RefreshReadyState();
    }

    private async void OnChooseFileClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || !StorageProvider.CanOpen)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz film lub plik napisów",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Filmy i napisy")
                {
                    Patterns = ["*.mkv", "*.mp4", "*.mov", "*.avi", "*.webm", "*.m4v", "*.ts", "*.mts", "*.m2ts", "*.srt", "*.ass", "*.ssa", "*.vtt", "*.txt"]
                },
                FilePickerFileTypes.All
            ]
        });

        var file = files.FirstOrDefault();
        if (file is not null)
            await LoadInputAsync(file.Path.LocalPath);
    }

    private async void OnChooseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || !StorageProvider.CanOpen)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder z filmami lub napisami",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder is not null)
            LoadFolder(folder.Path.LocalPath);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        OnDragOver(sender, e);
        if (e.DragEffects != DragDropEffects.None)
            DropZone.BorderBrush = _activeDropBrush;
    }

    private void OnDragLeave(object? sender, DragEventArgs e) => DropZone.BorderBrush = _normalDropBrush;

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = !_busy && e.DataTransfer.Formats.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        DropZone.BorderBrush = _normalDropBrush;
        if (_busy || !e.DataTransfer.Formats.Contains(DataFormat.File))
            return;

        var item = e.DataTransfer.TryGetFiles()?.FirstOrDefault();
        if (item is null)
            return;

        var path = item.Path.LocalPath;
        if (Directory.Exists(path))
            LoadFolder(path);
        else
            await LoadInputAsync(path);
    }

    private void LoadFolder(string path)
    {
        ResetOutput();
        ResetTrackSelection();
        _inputPath = null;
        _inputFolder = null;
        _folderItemCount = 0;

        if (!Directory.Exists(path))
        {
            SetStatus("Nie można znaleźć wybranego folderu.", StatusKind.Error);
            RefreshReadyState();
            return;
        }

        try
        {
            var queue = _folderQueuePlanner.Plan(path, ExportTxtCheckBox.IsChecked == true);
            _inputFolder = path;
            _folderItemCount = queue.Count;
            SelectedFileText.Text = $"{Path.GetFileName(path)} · {queue.Count} plików w kolejce";
            LiveProgressPanel.IsVisible = true;
            BatchTitleText.Text = "Folder";
            BatchCurrentText.Text = path;
            BatchDetailText.Text = queue.Count == 0
                ? "Brak obsługiwanych plików do tłumaczenia."
                : "Przetwarzanie będzie sekwencyjne; istniejące wyniki zostaną pominięte.";
            SetStatus(queue.Count == 0
                ? "W folderze nie znaleziono obsługiwanych plików."
                : $"Folder gotowy — {queue.Count} plików w kolejce.",
                queue.Count == 0 ? StatusKind.Error : StatusKind.Normal);
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyError(ex), StatusKind.Error);
        }

        RefreshReadyState();
    }

    private async Task LoadInputAsync(string path)
    {
        ResetOutput();
        ResetTrackSelection();
        _inputFolder = null;
        _folderItemCount = 0;

        if (!File.Exists(path))
        {
            SetStatus("Nie można znaleźć wybranego pliku.", StatusKind.Error);
            _inputPath = null;
            RefreshReadyState();
            return;
        }

        if (!TranslationPipeline.IsSupportedInput(path))
        {
            SetStatus("Nieobsługiwany format. Wybierz film albo SRT, ASS, SSA, VTT lub TXT.", StatusKind.Error);
            _inputPath = null;
            SelectedFileText.Text = Path.GetFileName(path);
            RefreshReadyState();
            return;
        }

        _inputPath = path;
        SelectedFileText.Text = Path.GetFileName(path);

        if (!TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(path)))
        {
            SetStatus(Path.GetExtension(path).Equals(".txt", StringComparison.OrdinalIgnoreCase)
                ? "Plik TXT gotowy do tłumaczenia. Wynik zostanie zapisany jako TXT."
                : "Plik napisów gotowy do tłumaczenia.", StatusKind.Normal);
            RefreshReadyState();
            return;
        }

        BeginOperation("Analizuję ścieżki napisów…", indeterminate: true);
        try
        {
            _tracks = await _mediaProbe.ProbeAsync(path, new Progress<string>(message => SetStatus(message, StatusKind.Normal)), _operationCancellation!.Token);
            TrackPanel.IsVisible = true;
            TrackComboBox.ItemsSource = _tracks;

            if (_tracks.Count == 0)
            {
                SetStatus("Ten film nie zawiera żadnej ścieżki napisów.", StatusKind.Error);
                TrackHintText.Text = "Napisy nie będą tworzone z dźwięku — ta aplikacja nie używa transkrypcji.";
                return;
            }

            var selected = FfprobeParser.ChooseDefault(_tracks);
            TrackComboBox.SelectedItem = selected ?? _tracks.FirstOrDefault();

            if (_tracks.All(track => !track.IsText))
            {
                SetStatus("Znaleziono tylko napisy obrazkowe (np. PGS/VobSub). Ta wersja nie używa OCR.", StatusKind.Error);
                TrackHintText.Text = "Wybierz film z tekstową ścieżką napisów albo załaduj osobny plik SRT/ASS/VTT.";
            }
            else if (selected is not null && selected.Language is "eng" or "en")
            {
                SetStatus("Znaleziono angielskie napisy tekstowe — wybrano je automatycznie.", StatusKind.Normal);
                TrackHintText.Text = "Możesz wybrać inną tekstową ścieżkę z listy.";
            }
            else
            {
                SetStatus("Znaleziono napisy. Sprawdź wybraną ścieżkę przed tłumaczeniem.", StatusKind.Normal);
                TrackHintText.Text = "Plik nie wskazuje jednoznacznie angielskiej ścieżki; wybierz właściwą ręcznie.";
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Anulowano analizę pliku.", StatusKind.Normal);
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyError(ex), StatusKind.Error);
        }
        finally
        {
            EndOperation();
            RefreshReadyState();
        }
    }

    private void ResetTrackSelection()
    {
        _tracks = [];
        TrackComboBox.ItemsSource = null;
        TrackPanel.IsVisible = false;
    }

    private void OnTrackSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TrackComboBox.SelectedItem is SubtitleTrack track && !track.IsText)
            SetStatus("Ta ścieżka jest obrazkowa i nie może zostać przetłumaczona bez OCR.", StatusKind.Error);
        RefreshReadyState();
    }

    private async void OnProviderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;

        ApplyProviderUi(useDefaults: true);
        await SaveSettingsAsync();
        RefreshReadyState();
    }

    private void ApplyProviderUi(bool useDefaults)
    {
        var provider = ProviderComboBox.SelectedItem as string ?? "Gemini";
        ModelTextBox.IsEnabled = provider != "DeepL";

        switch (provider)
        {
            case "DeepL":
                if (useDefaults) ModelTextBox.Text = string.Empty;
                if (useDefaults || string.IsNullOrWhiteSpace(BaseUrlTextBox.Text)) BaseUrlTextBox.Text = "https://api-free.deepl.com";
                ApiKeyHintText.Text = "Wymagany klucz DeepL API. Klucz nie jest zapisywany na dysku.";
                BaseUrlHintText.Text = "DeepL API Free. Dla planu Pro użyj https://api.deepl.com";
                break;
            case "Gemini":
                if (useDefaults || string.IsNullOrWhiteSpace(ModelTextBox.Text)) ModelTextBox.Text = "gemini-3.8-flash";
                if (useDefaults || string.IsNullOrWhiteSpace(BaseUrlTextBox.Text)) BaseUrlTextBox.Text = "https://generativelanguage.googleapis.com/v1beta";
                ApiKeyHintText.Text = "Wymagany klucz Gemini API. Klucz nie jest zapisywany na dysku.";
                BaseUrlHintText.Text = "Google Gemini Interactions API.";
                break;
            case "Claude":
                if (useDefaults || string.IsNullOrWhiteSpace(ModelTextBox.Text)) ModelTextBox.Text = "claude-sonnet-5";
                if (useDefaults || string.IsNullOrWhiteSpace(BaseUrlTextBox.Text)) BaseUrlTextBox.Text = "https://api.anthropic.com";
                ApiKeyHintText.Text = "Wymagany klucz Claude API. Klucz nie jest zapisywany na dysku.";
                BaseUrlHintText.Text = "Anthropic Messages API.";
                break;
            default:
                if (useDefaults || string.IsNullOrWhiteSpace(ModelTextBox.Text)) ModelTextBox.Text = "gpt-5.6-luna";
                if (useDefaults || string.IsNullOrWhiteSpace(BaseUrlTextBox.Text)) BaseUrlTextBox.Text = "https://api.openai.com/v1";
                ApiKeyHintText.Text = "Klucz jest wymagany dla OpenAI, ale może pozostać pusty dla lokalnego Ollama/LM Studio.";
                BaseUrlHintText.Text = "Ollama: http://localhost:11434/v1 · LM Studio: zwykle http://localhost:1234/v1";
                break;
        }
    }

    private void OnRevealKeyChanged(object? sender, RoutedEventArgs e) => ApiKeyTextBox.RevealPassword = RevealKeyCheckBox.IsChecked == true;

    private async void OnTranslateClick(object? sender, RoutedEventArgs e)
    {
        if (_busy || (_inputPath is null && _inputFolder is null))
            return;

        var providerName = ProviderComboBox.SelectedItem as string ?? "Gemini";
        ITranslationProvider provider;
        try
        {
            provider = ProviderFactory.Create(
                _httpClient,
                providerName,
                ApiKeyTextBox.Text ?? string.Empty,
                ModelTextBox.Text ?? string.Empty,
                BaseUrlTextBox.Text ?? string.Empty,
                _appLogger);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
            return;
        }

        await SaveSettingsAsync();
        ResetOperationProgress();
        BeginOperation("Przygotowuję napisy…", indeterminate: false);

        try
        {
            if (_inputFolder is not null)
                await TranslateFolderAsync(provider);
            else
                await TranslateSingleFileAsync(provider);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Tłumaczenie anulowane.", StatusKind.Normal);
        }
        catch (Exception ex)
        {
            SetStatus(ToFriendlyError(ex), StatusKind.Error);
        }
        finally
        {
            EndOperation(keepProgress: _lastOutputPath is not null || _lastOutputFolder is not null);
            RefreshReadyState();
        }
    }

    private async Task TranslateSingleFileAsync(ITranslationProvider provider)
    {
        var inputPath = _inputPath!;
        LiveProgressPanel.IsVisible = true;
        BatchTitleText.Text = "Tłumaczenie";
        BatchCurrentText.Text = Path.GetFileName(inputPath);
        BatchDetailText.Text = "Przygotowanie…";

        var selectedTrack = TrackComboBox.SelectedItem as SubtitleTrack;
        var translationProgress = new Progress<TranslationProgress>(ReportTranslationProgress);
        var statusProgress = new Progress<string>(message => SetStatus(message, StatusKind.Normal));

        var result = await ((ITranslationPipeline)_pipeline).TranslateAsync(
            inputPath,
            selectedTrack,
            provider,
            ExportTxtCheckBox.IsChecked == true,
            translationProgress,
            statusProgress,
            _operationCancellation!.Token);

        _lastOutputPath = result.PrimaryOutputPath;
        ProgressBar.Value = 100;
        BatchDetailText.Text = $"Segment {result.SegmentCount} / {result.SegmentCount} · gotowe";
        SetStatus($"Gotowe — przetłumaczono {result.SegmentCount} kwestii. Zapisano {Path.GetFileName(result.PrimaryOutputPath)}", StatusKind.Success);
        OpenFolderButton.Content = "Pokaż plik";
        OpenFolderButton.IsVisible = true;
    }

    private async Task TranslateFolderAsync(ITranslationProvider provider)
    {
        var folder = _inputFolder!;
        _lastOutputFolder = folder;
        LiveProgressPanel.IsVisible = true;
        BatchTitleText.Text = $"Folder · {_folderItemCount} plików";
        BatchCurrentText.Text = Path.GetFileName(folder);
        BatchDetailText.Text = "Uruchamiam kolejkę…";

        var folderProgress = new Progress<FolderBatchProgress>(ReportFolderProgress);
        var result = await _folderBatch.TranslateFolderAsync(
            folder,
            provider,
            ExportTxtCheckBox.IsChecked == true,
            folderProgress,
            _operationCancellation!.Token);

        ProgressBar.Value = 100;
        BatchCurrentText.Text = Path.GetFileName(folder);
        BatchDetailText.Text = $"Przetłumaczone {result.Translated} · pominięte {result.Skipped} · błędy {result.Failed}";
        SetStatus(
            $"Gotowe — przetłumaczone {result.Translated} · pominięte {result.Skipped} · błędy {result.Failed}.",
            result.Failed > 0 ? StatusKind.Error : StatusKind.Success);
        OpenFolderButton.Content = "Otwórz folder";
        OpenFolderButton.IsVisible = true;
    }

    private void ReportTranslationProgress(TranslationProgress value)
    {
        _activeTranslationProgress = value;
        LiveProgressPanel.IsVisible = true;
        BatchDetailText.Text = ProgressDisplayFormatter.Format(value, DateTimeOffset.UtcNow);
        ProgressBar.Value = value.TotalSegments == 0
            ? 0
            : Math.Clamp((double)value.CompletedSegments / value.TotalSegments * 100, 0, 100);
        UpdateElapsedTimer(value);
    }

    private void ReportFolderProgress(FolderBatchProgress value)
    {
        LiveProgressPanel.IsVisible = true;
        BatchTitleText.Text = $"Folder · plik {value.FileIndex} / {value.FileCount}";
        BatchCurrentText.Text = value.FileName;

        if (value.Translation is not null)
        {
            _activeTranslationProgress = value.Translation;
            BatchDetailText.Text = ProgressDisplayFormatter.Format(value.Translation, DateTimeOffset.UtcNow);
            var insideFile = value.Translation.TotalSegments == 0
                ? 0
                : (double)value.Translation.CompletedSegments / value.Translation.TotalSegments;
            ProgressBar.Value = value.FileCount == 0
                ? 0
                : Math.Clamp(((value.FileIndex - 1) + insideFile) / value.FileCount * 100, 0, 100);
            UpdateElapsedTimer(value.Translation);
            return;
        }

        _activeTranslationProgress = null;
        _elapsedTimer.Stop();
        BatchDetailText.Text = StageLabel(value.Stage);
        var fileFinished = value.Stage is "completed" or "skipped" or "failed";
        ProgressBar.Value = value.FileCount == 0
            ? 0
            : Math.Clamp((double)(fileFinished ? value.FileIndex : value.FileIndex - 1) / value.FileCount * 100, 0, 100);
    }

    private void UpdateElapsedTimer(TranslationProgress progress)
    {
        if (progress.WaitingForProvider)
        {
            if (!_elapsedTimer.IsEnabled)
                _elapsedTimer.Start();
        }
        else
        {
            _elapsedTimer.Stop();
        }
    }

    private void RefreshElapsedText()
    {
        if (_activeTranslationProgress is not { WaitingForProvider: true } progress)
        {
            _elapsedTimer.Stop();
            return;
        }

        BatchDetailText.Text = ProgressDisplayFormatter.Format(progress, DateTimeOffset.UtcNow);
    }

    private static string StageLabel(string stage) => stage switch
    {
        "queued" => "W kolejce…",
        "probing" => "Analizuję ścieżki napisów…",
        "translating" => "Przygotowuję tłumaczenie…",
        "completed" => "Gotowe",
        "skipped" => "Pominięto",
        "failed" => "Błąd — przechodzę do następnego pliku",
        _ => stage
    };

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _operationCancellation?.Cancel();

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_lastOutputPath is not null && File.Exists(_lastOutputPath))
            {
                RevealPath(_lastOutputPath, selectFile: true);
                return;
            }

            if (_lastOutputFolder is not null && Directory.Exists(_lastOutputFolder))
                RevealPath(_lastOutputFolder, selectFile: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Nie udało się otworzyć folderu: {ex.Message}", StatusKind.Error);
        }
    }

    private void OnOpenLogClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(_appLogger.LogPath))
                _appLogger.Info("log_created", ("result", "manual_open"));
            RevealPath(_appLogger.LogPath, selectFile: true);
        }
        catch (Exception ex)
        {
            SetStatus($"Nie udało się otworzyć logu: {ex.Message}", StatusKind.Error);
        }
    }

    private static void RevealPath(string path, bool selectFile)
    {
        if (OperatingSystem.IsWindows())
        {
            var arguments = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", arguments)
            {
                UseShellExecute = true
            });
            return;
        }

        var target = selectFile ? Path.GetDirectoryName(path) ?? Environment.CurrentDirectory : path;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true
        });
    }

    private void BeginOperation(string message, bool indeterminate)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _busy = true;
        ChooseFileButton.IsEnabled = false;
        ChooseFolderButton.IsEnabled = false;
        ProviderComboBox.IsEnabled = false;
        ModelTextBox.IsEnabled = false;
        ApiKeyTextBox.IsEnabled = false;
        BaseUrlTextBox.IsEnabled = false;
        ExportTxtCheckBox.IsEnabled = false;
        TrackComboBox.IsEnabled = false;
        TranslateButton.IsEnabled = false;
        CancelButton.IsVisible = true;
        ProgressBar.IsIndeterminate = indeterminate;
        if (!indeterminate) ProgressBar.Value = 0;
        SetStatus(message, StatusKind.Normal);
    }

    private void EndOperation(bool keepProgress = false)
    {
        _elapsedTimer.Stop();
        _activeTranslationProgress = null;
        _busy = false;
        CancelButton.IsVisible = false;
        ChooseFileButton.IsEnabled = true;
        ChooseFolderButton.IsEnabled = true;
        ProviderComboBox.IsEnabled = true;
        ApiKeyTextBox.IsEnabled = true;
        BaseUrlTextBox.IsEnabled = true;
        ExportTxtCheckBox.IsEnabled = true;
        TrackComboBox.IsEnabled = true;
        ProgressBar.IsIndeterminate = false;
        if (!keepProgress) ProgressBar.Value = 0;
        ApplyProviderUi(useDefaults: false);
        _operationCancellation?.Dispose();
        _operationCancellation = null;
    }

    private void RefreshReadyState()
    {
        if (_busy)
        {
            TranslateButton.IsEnabled = false;
            return;
        }

        if (_inputFolder is not null)
        {
            TranslateButton.IsEnabled = _folderItemCount > 0;
            return;
        }

        if (_inputPath is null || !TranslationPipeline.IsSupportedInput(_inputPath))
        {
            TranslateButton.IsEnabled = false;
            return;
        }

        if (TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(_inputPath)))
        {
            TranslateButton.IsEnabled = TrackComboBox.SelectedItem is SubtitleTrack { IsText: true };
            return;
        }

        TranslateButton.IsEnabled = true;
    }

    private async Task SaveSettingsAsync()
    {
        if (_loadingSettings)
            return;

        var settings = new AppSettings
        {
            Provider = ProviderComboBox.SelectedItem as string ?? "Gemini",
            Model = ModelTextBox.Text ?? string.Empty,
            BaseUrl = BaseUrlTextBox.Text ?? string.Empty,
            ExportTxt = ExportTxtCheckBox.IsChecked == true
        };
        await _settingsStore.SaveAsync(settings);
    }

    private void ResetOperationProgress()
    {
        _lastOutputPath = null;
        _lastOutputFolder = null;
        _activeTranslationProgress = null;
        _elapsedTimer.Stop();
        OpenFolderButton.IsVisible = false;
        ProgressBar.Value = 0;
    }

    private void ResetOutput()
    {
        ResetOperationProgress();
        LiveProgressPanel.IsVisible = false;
    }

    private void SetStatus(string message, StatusKind kind)
    {
        StatusText.Text = message;
        StatusText.Foreground = kind switch
        {
            StatusKind.Error => _errorBrush,
            StatusKind.Success => _successBrush,
            _ => _mutedBrush
        };
    }

    private static string ToFriendlyError(Exception ex)
    {
        if (ex is HttpRequestException)
            return "Błąd połączenia z usługą tłumaczącą. Sprawdź klucz API, Base URL i połączenie z internetem.";
        if (ex is UnauthorizedAccessException)
            return "Brak uprawnień do odczytu lub zapisu pliku.";
        if (ex is IOException)
            return "Nie udało się odczytać lub zapisać pliku. Sprawdź, czy nie jest używany przez inny program.";
        return ex.Message;
    }

    private enum StatusKind
    {
        Normal,
        Success,
        Error
    }
}
