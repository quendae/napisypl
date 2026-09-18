using Avalonia.Controls;
using NapisyPL.Shell;

namespace NapisyPL;

public partial class MainWindow
{
    /// <summary>Wires the Options menu toggles; they flip themselves (ToggleType="CheckBox").</summary>
    private void CompleteEnhancedControlInitialization()
    {
        EnhancedMenuItem.PropertyChanged += async (_, change) =>
        {
            if (change.Property != MenuItem.IsCheckedProperty || _loadingSettings)
                return;

            // Strict mode only makes sense on top of the correction itself.
            if (!EnhancedMenuItem.IsChecked && HardVoiceMenuItem.IsChecked)
                HardVoiceMenuItem.IsChecked = false;

            ApplyGenderCorrectionOptions();
            SetStatus(EnhancedMenuItem.IsChecked
                    ? "Korekta rodzaju z głosu włączona."
                    : "Korekta rodzaju z głosu wyłączona — zostaje wynik tłumacza.",
                StatusKind.Normal);
            await SaveSettingsAsync();
        };

        HardVoiceMenuItem.PropertyChanged += async (_, change) =>
        {
            if (change.Property != MenuItem.IsCheckedProperty || _loadingSettings)
                return;

            if (HardVoiceMenuItem.IsChecked && !EnhancedMenuItem.IsChecked)
                EnhancedMenuItem.IsChecked = true;

            ApplyGenderCorrectionOptions();
            SetStatus(HardVoiceMenuItem.IsChecked
                    ? "Tylko pewne rozpoznanie: formy zmieniamy wyłącznie przy wyraźnym głosie."
                    : "Korekta rodzaju używa też słabszych wskazówek.",
                StatusKind.Normal);
            await SaveSettingsAsync();
        };

        AskUncertainMenuItem.PropertyChanged += async (_, change) =>
        {
            if (change.Property != MenuItem.IsCheckedProperty || _loadingSettings)
                return;

            ApplyGenderCorrectionOptions();
            if (!AskUncertainMenuItem.IsChecked)
                GenderReviewButton.IsVisible = false;
            SetStatus(AskUncertainMenuItem.IsChecked
                    ? "Niepewne kwestie trafią do ciebie po tłumaczeniu."
                    : "Niepewne kwestie zostają tak, jak rozstrzygnął je algorytm.",
                StatusKind.Normal);
            await SaveSettingsAsync();
        };

        ExportTxtMenuItem.PropertyChanged += (_, change) =>
        {
            if (change.Property == MenuItem.IsCheckedProperty && _inputFolder is not null)
                LoadFolder(_inputFolder);
        };

        if (OperatingSystem.IsWindows())
        {
            ExplorerMenuItem.IsChecked = ExplorerContextMenu.IsRegistered();
            RefreshExplorerTip();
            ExplorerMenuItem.PropertyChanged += (_, change) =>
            {
                if (change.Property != MenuItem.IsCheckedProperty)
                    return;

                try
                {
                    if (ExplorerMenuItem.IsChecked)
                    {
                        ExplorerContextMenu.Register();
                        SetStatus("Dodano „Szukaj napisów z SubFlow” do menu Eksploratora. W Windows 11 jest pod „Pokaż więcej opcji”.", StatusKind.Success);
                    }
                    else
                    {
                        ExplorerContextMenu.Unregister();
                        SetStatus("Usunięto SubFlow z menu Eksploratora.", StatusKind.Normal);
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                {
                    SetStatus("Nie udało się zmienić menu Eksploratora: " + exception.Message, StatusKind.Error);
                }

                RefreshExplorerTip();
            };
        }
        else
        {
            ExplorerMenuItem.IsVisible = false;
            ExplorerTipCard.IsVisible = false;
        }
    }

    private void OnEnableExplorerMenuClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) =>
        ExplorerMenuItem.IsChecked = true;

    private void RefreshExplorerTip()
    {
        var registered = OperatingSystem.IsWindows() && ExplorerContextMenu.IsRegistered();
        EnableExplorerMenuButton.IsVisible = !registered;
        ExplorerTipText.Text = registered
            ? "Włączone: kliknij prawym na film lub folder i wybierz „Szukaj napisów z SubFlow”. W Windows 11 jest pod „Pokaż więcej opcji”."
            : "Kliknij prawym na film lub folder i wybierz „Szukaj napisów z SubFlow”. Ikona w obszarze powiadomień robi to samo.";
    }

    private void ApplyGenderCorrectionOptions() =>
        _services.ApplyGenderCorrection(
            EnhancedMenuItem.IsChecked, HardVoiceMenuItem.IsChecked, AskUncertainMenuItem.IsChecked);
}
