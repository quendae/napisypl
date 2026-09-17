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
        detail.Text = request.PolishCandidate is not null
            ? $"Dostawca: {request.PolishCandidate.Provider}\n" +
              $"Pokrycie: {analysis?.MatchedCueCoverage:P0} · przesunięcie: {analysis?.Transform.Offset.TotalMilliseconds:F0} ms · " +
              $"skala: {analysis?.Transform.Scale:F6} · P90: {analysis?.P90Residual.TotalMilliseconds:F0} ms\n" +
              $"Ocena dopasowania: {analysis?.Decision}."
            : request.PolishSearchReportedNoSubtitles
                ? "QNapi nie znalazło polskich napisów. Wybierz lokalny plik albo kontynuuj z angielskimi napisami."
                : "QNapi nie zwróciło polskiego pliku. Możesz wybrać inną wersję albo kontynuować z angielskimi napisami.";

        var buttons = new List<Button>();
        var interactiveQnapiAvailable = !request.PolishSearchReportedNoSubtitles;
        Button? interactiveQnapiButton = null;
        var actionRunning = false;
        var closed = false;
        CancellationTokenSource? activeActionCancellation = null;
        dialog.Closed += (_, _) =>
        {
            closed = true;
            activeActionCancellation?.Cancel();
        };

        void Close(SubtitleFallbackChoice choice)
        {
            if (closed || !dialog.IsVisible)
                return;
            activeActionCancellation?.Cancel();
            closed = true;
            dialog.Close(choice);
        }

        bool CanContinue() => !closed && dialog.IsVisible;

        Button AddButton(string text, Func<CancellationToken, Task<SubtitleFallbackChoice?>> action)
        {
            var button = new Button { Content = text, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) => _ = RunActionAsync(action);
            buttons.Add(button);
            actions.Children.Add(button);
            return button;
        }

        async Task RunActionAsync(Func<CancellationToken, Task<SubtitleFallbackChoice?>> action)
        {
            if (actionRunning || !CanContinue())
                return;
            actionRunning = true;
            SetButtonsEnabled(false);
            using var actionCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            activeActionCancellation = actionCancellation;
            try
            {
                var choice = await action(actionCancellation.Token);
                if (choice is not null && CanContinue())
                    Close(choice);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Close(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
            }
            catch (Exception ex)
            {
                if (CanContinue())
                    detail.Text = "Nie udało się wykonać wybranej akcji: " + ex.Message;
            }
            finally
            {
                if (ReferenceEquals(activeActionCancellation, actionCancellation))
                    activeActionCancellation = null;
                actionRunning = false;
                if (CanContinue())
                    SetButtonsEnabled(true);
            }
        }

        interactiveQnapiButton = AddButton("Otwórz QNapi i wybierz napisy", async actionToken =>
        {
            var path = videoPath();
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Nie wybrano filmu dla interaktywnego QNapi.");
            var result = await interactiveDownloader.DownloadInteractiveAsync(path, SubtitleLanguage.Polish, status, actionToken);
            if (result.ProviderReportedNoSubtitles)
            {
                interactiveQnapiAvailable = false;
                detail.Text = "QNapi nie znalazło polskich napisów. Wybierz inną alternatywę albo przejdź do tłumaczenia.";
                return null;
            }
            if (result.Subtitles is null)
            {
                detail.Text = "Zamknięto wybór QNapi bez zapisania napisów. Wybierz inną alternatywę albo przejdź do tłumaczenia.";
                return null;
            }
            return new SubtitleFallbackChoice(SubtitleFallbackAction.DownloadInteractivePolish, InteractivePolish: result.Subtitles);
        });
        interactiveQnapiButton.IsEnabled = interactiveQnapiAvailable;

        AddButton("Wybierz lokalny plik SRT", async _ =>
        {
            if (!owner.StorageProvider.CanOpen)
                return null;
            var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Wybierz polskie napisy SRT",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Napisy SRT") { Patterns = ["*.srt"] }]
            });
            var file = files.FirstOrDefault();
            return file is null
                ? null
                : new SubtitleFallbackChoice(SubtitleFallbackAction.UseLocalPolishSrt, file.Path.LocalPath);
        });

        if (request.PolishCandidate is not null && analysis?.Decision == SubtitleSyncDecision.NeedsReview)
        {
            AddButton("Zastosuj zalecaną korektę czasu", _ =>
            {
                return Task.FromResult<SubtitleFallbackChoice?>(new SubtitleFallbackChoice(SubtitleFallbackAction.ApplyRecommendedTransform));
            });
            AddButton("Użyj bez zmiany czasu", _ =>
            {
                return Task.FromResult<SubtitleFallbackChoice?>(new SubtitleFallbackChoice(SubtitleFallbackAction.UseWithoutChanges));
            });
        }

        if (request.HasEmbeddedTextTrack)
            AddButton(request.HasAutomaticallyTranslatableEmbeddedEnglish
                    ? "Przetłumacz osadzoną angielską ścieżkę"
                    : "Przetłumacz wybraną ścieżkę z filmu", _ =>
            {
                return Task.FromResult<SubtitleFallbackChoice?>(new SubtitleFallbackChoice(SubtitleFallbackAction.TranslateEmbeddedEnglish));
            });

        AddButton("Kontynuuj z angielskimi napisami", _ =>
        {
            return Task.FromResult<SubtitleFallbackChoice?>(new SubtitleFallbackChoice(SubtitleFallbackAction.ContinueWithEnglishFallback));
        });

        AddButton("Anuluj", _ =>
        {
            return Task.FromResult<SubtitleFallbackChoice?>(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel));
        });

        using var registration = cancellationToken.Register(() =>
            Dispatcher.UIThread.Post(() => Close(new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel))));
        return await dialog.ShowDialog<SubtitleFallbackChoice>(owner)
            ?? new SubtitleFallbackChoice(SubtitleFallbackAction.Cancel);

        void SetButtonsEnabled(bool enabled)
        {
            foreach (var button in buttons)
                button.IsEnabled = enabled;
            if (interactiveQnapiButton is not null)
                interactiveQnapiButton.IsEnabled = enabled && interactiveQnapiAvailable;
        }
    }
}
