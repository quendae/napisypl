using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Models;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Review;

/// <summary>
/// The lines the automatic review could not settle: what the Polish says, what the voice in the
/// film suggests instead, and the few seconds of audio needed to tell which one is right.
/// </summary>
public partial class GenderReviewWindow : Window
{
    private readonly AppServices _services = AppServices.Shared;
    private readonly GenderReviewQueueFile _queue;
    private readonly CuePlayer _player;

    public ObservableCollection<GenderReviewItem> Questions { get; } = [];

    public GenderReviewWindow(GenderReviewQueueFile queue)
    {
        ArgumentNullException.ThrowIfNull(queue);
        _queue = queue;
        _player = new CuePlayer(_services.Ffmpeg);

        InitializeComponent();
        Shell.DarkTitleBar.Apply(this);

        foreach (var candidate in queue.Cues)
            Questions.Add(new GenderReviewItem(candidate));
        QuestionList.ItemsSource = Questions;
        SummaryText.Text = $"{Questions.Count} {Plural(Questions.Count)} do rozstrzygnięcia w pliku " +
                           $"{Path.GetFileName(queue.SubtitlePath)}. Przy każdej z nich głos w filmie brzmi inaczej, " +
                           "niż sugeruje polski tekst.";

        Closed += (_, _) => _player.Dispose();
    }

    private static string Plural(int count) => count == 1 ? "kwestia" : "kwestii";

    private async void OnPlayClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { CommandParameter: GenderReviewItem item })
            return;

        StatusText.Text = string.Empty;
        try
        {
            await _player.PlayAsync(_queue.VideoPath, item.Candidate.Start, item.Candidate.End);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException
                                              or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            StatusText.Text = "Nie udało się odtworzyć fragmentu: " + exception.Message;
        }
    }

    private void OnKeepClick(object? sender, RoutedEventArgs e) => Answer(sender, accepted: false);

    private void OnAcceptClick(object? sender, RoutedEventArgs e) => Answer(sender, accepted: true);

    private void Answer(object? sender, bool accepted)
    {
        if (sender is Button { CommandParameter: GenderReviewItem item })
        {
            item.Accepted = accepted;
            var answered = Questions.Count(question => question.IsAnswered);
            StatusText.Text = answered == Questions.Count
                ? "Wszystkie kwestie rozstrzygnięte."
                : $"Rozstrzygnięto {answered} z {Questions.Count}.";
        }
    }

    /// <summary>Writes the accepted wordings back into the subtitle file and closes the queue.</summary>
    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var accepted = Questions.Where(question => question.TookProposal).ToArray();
        SaveButton.IsEnabled = false;
        try
        {
            if (accepted.Length > 0)
            {
                var cues = new SrtParser().Parse(await File.ReadAllTextAsync(_queue.SubtitlePath));
                var replacements = accepted.ToDictionary(question => question.CueId, question => question.Proposed);
                var updated = cues
                    .Select(cue => replacements.TryGetValue(cue.Index, out var text) ? cue with { Text = text } : cue)
                    .ToArray();
                await new SubtitleWriter().WriteSrtAsync(_queue.SubtitlePath, updated);
            }

            // Questions left open stay open; once every one has an answer the file is done with.
            var remaining = Questions
                .Where(question => !question.IsAnswered)
                .Select(question => question.Candidate)
                .ToArray();
            await GenderReviewQueue.WriteAsync(_queue with { Cues = remaining });

            Close(accepted.Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            StatusText.Text = "Nie udało się zapisać napisów: " + exception.Message;
            SaveButton.IsEnabled = true;
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(0);
}
