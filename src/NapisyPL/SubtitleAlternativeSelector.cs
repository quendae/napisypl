using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using NapisyPL.Core.Subtitles;

namespace NapisyPL;

/// <summary>Single-file modal for choosing a Polish subtitle alternative after automatic acquisition fails.</summary>
public sealed class SubtitleAlternativeSelector(
    Window owner,
    IInteractiveSubtitleDownloader interactiveDownloader,
    Func<string?> videoPath) : ISubtitleFallbackInteraction
{
    public async Task<SubtitleFallbackChoice> ChooseAsync(
        SubtitleFallbackRequest request,
        IProgress<string>? status = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new Window
        {
            Title = "Alternatywne napisy",
            Width = 520,
            MinWidth = 440,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var detail = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var actions = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var root = new StackPanel { Margin = new Avalonia.Thickness(20), Spacing = 12 };
        root.Children.Add(new TextBlock
        {
            Text = "Nie zapisaliśmy automatycznie znalezionych polskich napisów.",
            FontWeight = Avalonia.Media.FontWeight.SemiBold,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        root.Children.Add(detail);
        root.Children.Add(actions);
        dialog.Content = root;

        var analysis = request.TimingAnalysis;
        detail.Text = request.PolishCandidate is null
            ? "QNapi nie zwróciło polskiego pliku. Możesz wybrać inną wersję albo przejść do tłumaczenia angielskiej ścieżki."
            : $"Dostawca: {request.PolishCandidate.Provider}\n" +
              $"Pokrycie: {analysis?.MatchedCueCoverage:P0} · przesunięcie: {analysis?.Transform.Offset.TotalMilliseconds:F0} ms · " +
              $"skala: {analysis?.Transform.Scale:F6} · P90: {analysis?.P90Residual.TotalMilliseconds:F0} ms\n" +
              $"Ocena dopasowania: {analysis?.Decision}.";

        void Close(SubtitleFallbackChoice choice) => dialog.Close(choice);
        var buttons = new List<Button>();
        Button AddButton(string text, Func<Task> action)
        {
            var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += async (_, _) => await action();
            buttons.Add(button);
            actions.Children.Add(button);
            return button;
        }

        AddButton("Otwórz QNapi i wybierz napisy", async () =>
        {
            var path = videoPath();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Nie wybrano filmu dla interaktywnego QNapi.");
            SetButtonsEnabled(false);
            try
            {
                var chosen = await interactiveDownloader.DownloadInteractiveAsync(path, SubtitleLanguage.Polish, status, cancellationToken);
                if (chosen is null)
                {
                    detail.Text = "Nie wybrano pliku w QNapi. Wybierz inną alternatywę albo przejdź do tłumaczenia.";
                    SetButtonsEnabled(true);
                    return;
                }
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.DownloadInteractivePolish, InteractivePolish: chosen));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
            }
            catch (Exception ex)
            {
                detail.Text = "QNapi nie zwróciło użytecznych napisów: " + ex.Message;
                SetButtonsEnabled(true);
            }
        });

        AddButton("Wybierz lokalny plik SRT", async () =>
        {
            if (!owner.StorageProvider.CanOpen)
                return;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Wybierz polskie napisy SRT",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Napisy SRT") { Patterns = ["*.srt"] }]
            });
            var file = files.FirstOrDefault();
            if (file is not null)
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.UseLocalPolishSrt, file.Path.LocalPath));
        });

        if (request.PolishCandidate is not null && analysis?.Decision == SubtitleSyncDecision.NeedsReview)
        {
            AddButton("Zastosuj zalecaną korektę czasu", () =>
            {
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.ApplyRecommendedTransform));
                return Task.CompletedTask;
            });
            AddButton("Użyj bez zmiany czasu", () =>
            {
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.UseWithoutChanges));
                return Task.CompletedTask;
            });
        }

        if (request.HasEmbeddedTextTrack)
            AddButton(request.HasAutomaticallyTranslatableEmbeddedEnglish
                    ? "Przetłumacz osadzoną angielską ścieżkę"
                    : "Przetłumacz wybraną ścieżkę z filmu", () =>
            {
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.TranslateEmbeddedEnglish));
                return Task.CompletedTask;
            });

        AddButton("Anuluj", () =>
        {
            Close(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
            return Task.CompletedTask;
        });

        using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => Close(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel))));
        return await dialog.ShowDialog<SubtitleFallbackChoice>(owner);

        void SetButtonsEnabled(bool enabled)
        {
            foreach (var button in buttons)
                button.IsEnabled = enabled;
        }
    }
}
