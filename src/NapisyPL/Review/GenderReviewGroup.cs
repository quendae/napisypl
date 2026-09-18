using System.Collections.ObjectModel;

namespace NapisyPL.Review;

/// <summary>
/// The questions one voice asks, all pointing the same way. Hearing one of them is usually
/// enough to answer the rest, so they are decided together and only split when they disagree.
/// </summary>
public sealed class GenderReviewGroup(IReadOnlyList<GenderReviewItem> items)
{
    public ObservableCollection<GenderReviewItem> Items { get; } = [.. items];

    public bool IsShared => Items.Count > 1;

    public string Title => $"Ten sam głos · {Items.Count} {Plural(Items.Count)}";

    public string Note =>
        "Wszystkie przypisane jednemu rozmówcy i wszystkie w tę samą stronę, " +
        "więc jedna odpowiedź może załatwić całą grupę.";

    /// <summary>"2 kwestie" but "5 kwestii", and "12 kwestii" rather than "12 kwestie".</summary>
    public static string Plural(int count) => count == 1
        ? "kwestia"
        : count % 10 is >= 2 and <= 4 && count % 100 is < 12 or > 14 ? "kwestie" : "kwestii";

    public void Answer(bool accepted)
    {
        foreach (var item in Items)
            item.Accepted = accepted;
    }
}
