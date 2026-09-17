using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Search;

public enum BadgeState
{
    Pending,
    Working,
    Yes,
    Review,
    No
}

/// <summary>One video in the search window, with its two availability badges.</summary>
public sealed class SearchItem : INotifyPropertyChanged
{
    private BadgeState _polish = BadgeState.Pending;
    private BadgeState _machine = BadgeState.Pending;
    private string _polishText = "PL";
    private string _machineText = "Maszynowe";
    private string _detail = "W kolejce";
    private bool _isSelected = true;
    private bool _canSelect;
    private bool _canSyncPolish;

    public SearchItem(string videoPath)
    {
        VideoPath = videoPath;
        FileName = Path.GetFileName(videoPath);
        Folder = Path.GetDirectoryName(videoPath) ?? string.Empty;
    }

    public string VideoPath { get; }
    public string FileName { get; }
    public string Folder { get; }
    public SubtitleAvailability? Availability { get; set; }

    public BadgeState Polish { get => _polish; set { if (Set(ref _polish, value)) RaiseBadge(nameof(Polish)); } }
    public BadgeState Machine { get => _machine; set { if (Set(ref _machine, value)) RaiseBadge(nameof(Machine)); } }
    public string PolishText { get => _polishText; set => Set(ref _polishText, value); }
    public string MachineText { get => _machineText; set => Set(ref _machineText, value); }
    public string Detail { get => _detail; set => Set(ref _detail, value); }

    /// <summary>Whether this video is included when "Przetłumacz maszynowo" runs.</summary>
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public bool CanSelect { get => _canSelect; set => Set(ref _canSelect, value); }

    /// <summary>Polish fitted from another release during this session.</summary>
    public bool HasSyncedPolish { get; set; }

    /// <summary>No Polish yet: offer fitting Polish subtitles from another release.</summary>
    public bool CanSyncPolish { get => _canSyncPolish; set => Set(ref _canSyncPolish, value); }

    public Geometry PolishIcon => Badges.Icon(Polish);
    public IBrush PolishBrush => Badges.Foreground(Polish);
    public IBrush PolishBackground => Badges.Background(Polish);
    public Geometry MachineIcon => Badges.Icon(Machine);
    public IBrush MachineBrush => Badges.Foreground(Machine);
    public IBrush MachineBackground => Badges.Background(Machine);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseBadge(string prefix)
    {
        OnPropertyChanged(prefix + "Icon");
        OnPropertyChanged(prefix + "Brush");
        OnPropertyChanged(prefix + "Background");
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Badge look. State is never carried by colour alone: every state has its own icon
/// and its own label text.
/// </summary>
internal static class Badges
{
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#9AA5B8"));
    private static readonly IBrush Success = new SolidColorBrush(Color.Parse("#4FD1C5"));
    private static readonly IBrush Warning = new SolidColorBrush(Color.Parse("#FDB022"));
    private static readonly IBrush Ink = new SolidColorBrush(Color.Parse("#EEF2F8"));
    private static readonly IBrush NeutralSoft = new SolidColorBrush(Color.Parse("#14FFFFFF"));
    private static readonly IBrush SuccessSoft = new SolidColorBrush(Color.Parse("#1A4FD1C5"));
    private static readonly IBrush WarningSoft = new SolidColorBrush(Color.Parse("#1FFDB022"));

    private static readonly Geometry Check = Geometry.Parse("M20 6 9 17 4 12");
    private static readonly Geometry Cross = Geometry.Parse("M18 6 6 18 M6 6 18 18");
    private static readonly Geometry Alert = Geometry.Parse("M12 8 12 13 M12 16.5 12 16.6 M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0Z");
    private static readonly Geometry Dots = Geometry.Parse("M5 12 5.1 12 M12 12 12.1 12 M19 12 19.1 12");
    private static readonly Geometry Clock = Geometry.Parse("M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18Z M12 7 12 12 15 14");

    public static Geometry Icon(BadgeState state) => state switch
    {
        BadgeState.Yes => Check,
        BadgeState.No => Cross,
        BadgeState.Review => Alert,
        BadgeState.Working => Clock,
        _ => Dots
    };

    public static IBrush Foreground(BadgeState state) => state switch
    {
        BadgeState.Yes => Success,
        BadgeState.Review => Warning,
        BadgeState.Working => Ink,
        _ => Muted
    };

    public static IBrush Background(BadgeState state) => state switch
    {
        BadgeState.Yes => SuccessSoft,
        BadgeState.Review => WarningSoft,
        _ => NeutralSoft
    };
}
