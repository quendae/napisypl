using Avalonia.Controls;
using Avalonia.Threading;
using NapisyPL.Core.LocalTranslation;

namespace NapisyPL;

public partial class MainWindow
{
    private const string ArgosProviderName = "Local Argos (offline)";
    private bool _argosUiHooked;

    private void OnArgosWindowOpened(object? sender, EventArgs e)
    {
        var items = ProviderNames.Contains(ArgosProviderName, StringComparer.Ordinal)
            ? ProviderNames
            : ProviderNames.Append(ArgosProviderName).ToArray();
        ProviderComboBox.ItemsSource = items;

        if (!_argosUiHooked)
        {
            ProviderComboBox.SelectionChanged += OnArgosProviderSelectionChanged;
            _argosUiHooked = true;
        }

        // Existing OnOpened also loads settings. Run this after the current UI event cycle so
        // a previously selected Argos provider can be restored even though the legacy array
        // does not contain it yet.
        Dispatcher.UIThread.Post(async () =>
        {
            var settings = await _settingsStore.LoadAsync();
            if (string.Equals(settings.Provider, ArgosProviderName, StringComparison.Ordinal))
            {
                ProviderComboBox.SelectedItem = ArgosProviderName;
                ApplyArgosProviderUi();
                RefreshReadyState();
            }
        }, DispatcherPriority.Background);
    }

    private void OnArgosProviderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (string.Equals(ProviderComboBox.SelectedItem as string, ArgosProviderName, StringComparison.Ordinal))
            ApplyArgosProviderUi();
    }

    private void ApplyArgosProviderUi()
    {
        ModelTextBox.Text = "Argos Translate EN→PL 1.9";
        BaseUrlTextBox.Text = "lokalnie · CTranslate2";
        ApiKeyTextBox.Text = string.Empty;
        ModelTextBox.IsEnabled = false;
        ApiKeyTextBox.IsEnabled = false;
        BaseUrlTextBox.IsEnabled = false;
        RevealKeyCheckBox.IsEnabled = false;
        ApiKeyHintText.Text = "Bez klucza API. Tłumaczenie działa lokalnie na tym komputerze.";
        BaseUrlHintText.Text = "Pierwsze użycie pobierze model EN→PL ok. 67 MB. Model jest zachowywany w LocalAppData\\SubFlow\\argos.";
    }

    private async void OnArgosWindowClosed(object? sender, EventArgs e)
    {
        await ArgosRuntimeRegistry.DisposeAsync();
    }
}
