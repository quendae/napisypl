using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using NapisyPL.Core.Models;
using NapisyPL.Core.OfflineMt.Nllb;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Search;

/// <summary>
/// The Explorer context-menu and tray flow. For every video, without asking: Polish
/// from QNapi; if there is none, English inside the video; if there is none, English
/// from QNapi. When English was found, machine translation is offered as one button.
/// </summary>
public partial class SearchWindow : Window
{
    /// <summary>A folder picked by mistake (a whole drive) should not queue thousands of files.</summary>
    private const int MaximumQueuedVideos = 500;

    private readonly AppServices _services = AppServices.Shared;
    private readonly HashSet<string> _known = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _work;
    private bool _scanning;
    private bool _translating;

    public SearchWindow()
    {
        InitializeComponent();
        Shell.DarkTitleBar.Apply(this);
        DataContext = this;
        Closed += (_, _) => _work?.Cancel();
        // Row checkboxes enable or disable the translate button.
        Items.CollectionChanged += (_, args) =>
        {
            foreach (var item in args.NewItems?.OfType<SearchItem>() ?? [])
                item.PropertyChanged += (_, change) =>
                {
                    if (change.PropertyName is nameof(SearchItem.IsSelected) or nameof(SearchItem.CanSelect))
                        RefreshButtons();
                };
        };
    }

    public ObservableCollection<SearchItem> Items { get; } = [];

    public bool IsBusy => _scanning || _translating;

    /// <summary>Adds videos (or folders of videos) and scans whatever is new.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var video in ExpandToVideos(paths))
        {
            if (!_known.Add(video))
                continue;
            Items.Add(new SearchItem(video));
            added++;
        }

        RefreshLayout();
        if (added == 0 && Items.Count == 0)
        {
            SetStatus("Nie znaleziono tu plików wideo (MKV, MP4, MOV, AVI, WebM…).");
            return;
        }

        if (!_scanning)
            _ = ScanPendingAsync();
    }

    public async Task PickFileAsync()
    {
        if (!StorageProvider.CanOpen)
            return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Wybierz film",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Filmy")
                {
                    Patterns = TranslationPipeline.VideoExtensions.Select(extension => "*" + extension).ToArray()
                }
            ]
        });
        AddPaths(files.Select(file => file.Path.LocalPath));
    }

    public async Task PickFolderAsync()
    {
        if (!StorageProvider.CanOpen)
            return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Wybierz folder z filmami",
            AllowMultiple = false
        });
        AddPaths(folders.Select(folder => folder.Path.LocalPath));
    }

    private static IEnumerable<string> ExpandToVideos(IEnumerable<string> paths)
    {
        var count = 0;
        foreach (var path in paths)
        {
            IEnumerable<string> candidates;
            if (Directory.Exists(path))
            {
                try
                {
                    candidates = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true,
                            IgnoreInaccessible = true,
                            MaxRecursionDepth = 3
                        })
                        .Where(IsVideo)
                        .Order(StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    continue;
                }
            }
            else
            {
                candidates = File.Exists(path) && IsVideo(path) ? [path] : [];
            }

            foreach (var candidate in candidates)
            {
                if (count++ >= MaximumQueuedVideos)
                    yield break;
                yield return candidate;
            }
        }
    }

    private static bool IsVideo(string path) =>
        TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(path));

    private async Task ScanPendingAsync()
    {
        _scanning = true;
        _work ??= new CancellationTokenSource();
        var token = _work.Token;
        RefreshButtons();

        try
        {
            while (Items.FirstOrDefault(item => item.Availability is null && item.Polish == BadgeState.Pending) is { } item)
            {
                token.ThrowIfCancellationRequested();
                var index = Items.IndexOf(item) + 1;
                SetStatus($"Szukam napisów {index} z {Items.Count}: {item.FileName}");
                SetProgress(index - 1, Items.Count);
                await ScanAsync(item, token);
            }

            SetProgress(null, 0);
            SetStatus(SummaryAfterScan());
        }
        catch (OperationCanceledException)
        {
            SetProgress(null, 0);
            SetStatus("Zatrzymano wyszukiwanie.");
        }
        finally
        {
            _scanning = false;
            if (!_translating)
            {
                _work?.Dispose();
                _work = null;
            }
            RefreshButtons();
        }
    }

    private async Task ScanAsync(SearchItem item, CancellationToken token)
    {
        item.Polish = BadgeState.Working;
        item.PolishText = "PL…";
        item.Detail = "Szukam polskich napisów…";
        // Progress callbacks are posted; one can arrive after the result is shown and
        // would overwrite the summary with a stale "QNapi: …" line.
        var finished = false;
        var status = new Progress<string>(message =>
        {
            if (!finished)
                item.Detail = message;
        });

        try
        {
            SubtitleTrack? englishTrack = null;
            try
            {
                var tracks = await _services.MediaProbe.ProbeAsync(item.VideoPath, null, token);
                englishTrack = FfprobeParser.ChooseDefault(tracks) is { IsText: true } track &&
                               track.Language is { } language &&
                               (language.Equals("eng", StringComparison.OrdinalIgnoreCase) || language.Equals("en", StringComparison.OrdinalIgnoreCase))
                    ? track
                    : null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _services.Logger.Error("search_probe_failed", ("file", item.FileName), ("message", exception.Message));
            }

            var result = await _services.SubtitlePipeline.ScanAsync(item.VideoPath, englishTrack, status, token);
            finished = true;
            item.Availability = result;
            ShowResult(item, result);
            _services.Logger.Info("search_result",
                ("file", item.FileName),
                ("polish", result.Polish),
                ("english", result.English),
                ("problems", result.Problems.Count));
        }
        catch (OperationCanceledException)
        {
            item.Polish = BadgeState.Pending;
            item.PolishText = "PL";
            item.Detail = "Zatrzymano";
            throw;
        }
        catch (Exception exception)
        {
            finished = true;
            item.Polish = BadgeState.No;
            item.PolishText = "PL";
            item.Machine = BadgeState.No;
            item.Detail = "Błąd: " + exception.Message;
            item.Availability = new SubtitleAvailability(item.VideoPath, PolishSubtitleState.Missing, null, null,
                EnglishSubtitleSource.None, null, null, [exception.Message]);
            _services.Logger.Error("search_failed", ("file", item.FileName), ("message", exception.Message));
        }
    }

    private static void ShowResult(SearchItem item, SubtitleAvailability result)
    {
        (item.Polish, item.PolishText) = result.Polish switch
        {
            PolishSubtitleState.Existing or PolishSubtitleState.Saved => (BadgeState.Yes, "PL"),
            PolishSubtitleState.NeedsReview => (BadgeState.Review, "PL do sprawdzenia"),
            _ => (BadgeState.No, "Brak PL")
        };

        (item.Machine, item.MachineText) = result.CanTranslate
            ? (BadgeState.Yes, "Maszynowe")
            : (BadgeState.No, "Brak EN");

        item.CanSelect = result.CanTranslate && !result.HasPolish;
        item.IsSelected = item.CanSelect;
        item.Detail = result switch
        {
            { Polish: PolishSubtitleState.Existing } => "Polskie napisy już były obok filmu.",
            { Polish: PolishSubtitleState.Saved } => $"Zapisano polskie napisy ({result.PolishProvider}).",
            { Polish: PolishSubtitleState.NeedsReview, English: EnglishSubtitleSource.Embedded } =>
                $"Polskie z {result.PolishProvider} nie pasują czasem. Angielskie są w filmie.",
            { Polish: PolishSubtitleState.NeedsReview, English: EnglishSubtitleSource.Downloaded } =>
                $"Polskie z {result.PolishProvider} nie pasują czasem. Angielskie z {result.EnglishProvider}.",
            { Polish: PolishSubtitleState.NeedsReview } =>
                $"Polskie z {result.PolishProvider} nie pasują czasem, a angielskich nie znaleziono.",
            { English: EnglishSubtitleSource.Embedded } => "Brak polskich. Angielskie napisy są w filmie.",
            { English: EnglishSubtitleSource.Downloaded } => $"Brak polskich. Angielskie z {result.EnglishProvider}.",
            _ => "Nie znaleziono polskich ani angielskich napisów."
        };
    }

    private async void OnTranslateClick(object? sender, RoutedEventArgs e)
    {
        var queue = Items.Where(item => item.CanSelect && item.IsSelected && item.Availability is { CanTranslate: true }).ToArray();
        if (queue.Length == 0 || _translating)
            return;

        _translating = true;
        _work ??= new CancellationTokenSource();
        var token = _work.Token;
        RefreshButtons();

        var acquired = false;
        try
        {
            if (!await _services.TranslationGate.WaitAsync(0, token))
            {
                SetStatus("Czekam, aż skończy się inne tłumaczenie w SubFlow…");
                await _services.TranslationGate.WaitAsync(token);
            }
            acquired = true;

            var settings = await _services.Settings.LoadAsync();
            _services.ApplyGenderCorrection(settings.GenderCorrection, settings.StrictVoiceEvidence);
            var provider = await _services.CreateProviderFromSettingsAsync(token);

            var done = 0;
            var failed = 0;
            for (var position = 0; position < queue.Length; position++)
            {
                token.ThrowIfCancellationRequested();
                var item = queue[position];
                SetStatus($"Tłumaczę {position + 1} z {queue.Length}: {item.FileName}");
                SetProgress(position, queue.Length);
                item.Machine = BadgeState.Working;
                item.MachineText = "Tłumaczę…";
                item.CanSelect = false;

                var translationProgress = new Progress<TranslationProgress>(value =>
                {
                    if (value.TotalSegments > 0)
                    {
                        item.Detail = $"Tłumaczenie: {value.CompletedSegments} z {value.TotalSegments} kwestii";
                        SetProgress(position + (double)value.CompletedSegments / value.TotalSegments, queue.Length);
                    }
                });
                var translationFinished = false;
                var status = new Progress<string>(message =>
                {
                    if (!translationFinished)
                        item.Detail = message;
                });

                try
                {
                    var result = await _services.SubtitlePipeline.TranslateScannedAsync(
                        item.Availability!, provider, exportTxt: false, translationProgress, status, token);
                    translationFinished = true;
                    item.Machine = result.RequiresReview ? BadgeState.Review : BadgeState.Yes;
                    item.MachineText = result.RequiresReview ? "Do sprawdzenia" : "Przetłumaczone";
                    item.Polish = BadgeState.Yes;
                    item.PolishText = "PL";
                    item.Detail = result.RequiresReview
                        ? "Zapisano, ale tłumaczenie wymaga sprawdzenia."
                        : $"Zapisano {Path.GetFileName(result.PrimaryOutputPath)} · {result.SegmentCount} kwestii.";
                    done++;
                }
                catch (OperationCanceledException)
                {
                    item.Machine = BadgeState.Yes;
                    item.MachineText = "Maszynowe";
                    item.CanSelect = true;
                    item.Detail = "Zatrzymano tłumaczenie.";
                    throw;
                }
                catch (Exception exception)
                {
                    item.Machine = BadgeState.No;
                    item.MachineText = "Błąd";
                    item.Detail = "Błąd tłumaczenia: " + exception.Message;
                    failed++;
                    _services.Logger.Error("search_translate_failed", ("file", item.FileName), ("message", exception.Message));
                }
            }

            SetProgress(null, 0);
            SetStatus(failed == 0
                ? $"Gotowe. Przetłumaczono {done} z {queue.Length}."
                : $"Przetłumaczono {done} z {queue.Length}. Błędy: {failed} (szczegóły w logu).");
        }
        catch (OperationCanceledException)
        {
            SetProgress(null, 0);
            SetStatus("Zatrzymano tłumaczenie.");
        }
        finally
        {
            if (acquired)
                _services.TranslationGate.Release();
            _translating = false;
            if (!_scanning)
            {
                _work?.Dispose();
                _work = null;
            }
            RefreshButtons();
        }
    }

    private string SummaryAfterScan()
    {
        var polish = Items.Count(item => item.Availability?.HasPolish == true);
        var translatable = Items.Count(item => item.CanSelect);
        var nothing = Items.Count(item => item.Availability is { HasPolish: false, CanTranslate: false });
        var parts = new List<string> { $"Polskie napisy: {polish} z {Items.Count}." };
        if (translatable > 0)
            parts.Add($"Do przetłumaczenia maszynowo: {translatable}.");
        if (nothing > 0)
            parts.Add($"Bez napisów: {nothing}.");
        return string.Join(" ", parts);
    }

    private void RefreshLayout()
    {
        var hasItems = Items.Count > 0;
        EmptyState.IsVisible = !hasItems;
        ListScroller.IsVisible = hasItems;
        var folders = Items.Select(item => item.Folder).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        SummaryText.Text = !hasItems
            ? "Wybierz film lub folder."
            : folders.Length == 1
                ? $"{Items.Count} {VideoWord(Items.Count)} · {folders[0]}"
                : $"{Items.Count} {VideoWord(Items.Count)} z {folders.Length} folderów";
    }

    private static string VideoWord(int count) =>
        count == 1 ? "film" : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "filmy" : "filmów";

    private void RefreshButtons()
    {
        var selectable = Items.Count(item => item.CanSelect && item.IsSelected);
        TranslateButton.IsEnabled = !_translating && !_scanning && selectable > 0;
        TranslateButtonText.Text = selectable > 0 ? $"Przetłumacz maszynowo ({selectable})" : "Przetłumacz maszynowo";
        CancelButton.IsVisible = IsBusy;
        OpenFolderButton.IsEnabled = Items.Count > 0;
        AddFileButton.IsEnabled = !_translating;
        AddFolderButton.IsEnabled = !_translating;
    }

    private void SetStatus(string message) => StatusText.Text = message;

    private void SetProgress(double? done, int total)
    {
        Progress.IsVisible = done is not null && total > 0;
        if (done is not null && total > 0)
            Progress.Value = Math.Clamp(done.Value / total * 100, 0, 100);
    }

    private async void OnAddFileClick(object? sender, RoutedEventArgs e) => await PickFileAsync();

    private async void OnAddFolderClick(object? sender, RoutedEventArgs e) => await PickFolderAsync();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => _work?.Cancel();

    private void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var target = Items.FirstOrDefault()?.Folder;
        if (string.IsNullOrEmpty(target) || !Directory.Exists(target))
            return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{target}\"")
        {
            UseShellExecute = true
        });
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        RefreshLayout();
        RefreshButtons();
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Nothing else holds the GPU model once the last window is gone.
        if (App.Current is App app && !app.IsMainWindowVisible && _services.TranslationGate.CurrentCount == 1)
            await NllbRuntimeRegistry.DisposeAsync();
    }
}
