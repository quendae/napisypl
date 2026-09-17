using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NapisyPL.Core.OfflineMt.Nllb;
using NapisyPL.Core.Translation;

namespace NapisyPL;

public partial class MainWindow
{
    private const string NllbFastProviderName = "NLLB 600M — Fast";
    private const string NllbBalancedProviderName = "NLLB 1.3B — Balanced";
    private const string NllbQualityTestProviderName = "NLLB 3.3B — Quality Test";
    private const string MadladQualityProviderName = "MADLAD-400 3B — Quality";
    private const string LegacyNllbProviderName = "NLLB-200 600M (offline, benchmark)";
    private static readonly string[] RetiredLocalProviderNames =
    [
        "Local Argos (offline)",
        "Firefox/Bergamot (offline)",
        "OPUS-MT / Marian (offline)",
        "Local Qwen (offline)",
        "Local Qwen — pełne tłumaczenie (wolne, eksperymentalne)"
    ];
    private static readonly string[] LocalMtProviderNames =
    [
        MadladQualityProviderName,
        NllbFastProviderName,
        NllbBalancedProviderName,
        NllbQualityTestProviderName
    ];

    private bool _modernUiHooked;
    private bool _loadingSecretUi;

    private bool _providerListInitialized;

    private void OnArgosWindowOpened(object? sender, EventArgs e)
    {
        // Opened fires again when the window comes back from the tray; rebuilding the
        // list then would count as a provider switch and unload the GPU model.
        if (_providerListInitialized)
            return;
        _providerListInitialized = true;

        var items = LocalMtProviderNames
            .Concat(ProviderNames)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        ProviderComboBox.ItemsSource = items;

        if (!_modernUiHooked)
        {
            ProviderComboBox.SelectionChanged += OnModernProviderSelectionChanged;
            TranslateButton.Click += OnRememberApiKeyOnTranslate;
            _modernUiHooked = true;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            var settings = await _settingsStore.LoadAsync();
            var restoredProvider = settings.Provider == LegacyNllbProviderName ||
                                   RetiredLocalProviderNames.Contains(settings.Provider, StringComparer.Ordinal)
                ? MadladQualityProviderName
                : settings.Provider;
            if (items.Contains(restoredProvider, StringComparer.Ordinal))
                ProviderComboBox.SelectedItem = restoredProvider;

            ApplyModernProviderUi();
            await LoadRememberedApiKeyAsync();
            RefreshReadyState();
        }, DispatcherPriority.Background);
    }

    private async void OnModernProviderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;

        // Switching quality profiles must release the previous model before the
        // next one is loaded, otherwise multiple multi-GB models can remain in VRAM.
        await NllbRuntimeRegistry.DisposeAsync();
        ApplyModernProviderUi();
        await LoadRememberedApiKeyAsync();
        await SaveSettingsAsync();
    }

    private void ApplyModernProviderUi()
    {
        var provider = ProviderComboBox.SelectedItem as string ?? "Gemini";
        var profile = ProviderUiProfile.For(provider);

        ModelPanel.IsVisible = profile.ShowModel;
        ApiKeyPanel.IsVisible = profile.ShowApiKey;
        BaseUrlPanel.IsVisible = profile.ShowBaseUrl;
        RememberKeyCheckBox.IsVisible = profile.CanRememberApiKey;

        ProviderHintText.Text = provider switch
        {
            MadladQualityProviderName => "Zalecany. Działa lokalnie na karcie graficznej, bez internetu.",
            NllbFastProviderName => "Lokalny i najszybszy, ale słabszy. Licencja niekomercyjna.",
            NllbBalancedProviderName => "Lokalny, średnia jakość i szybkość. Licencja niekomercyjna.",
            NllbQualityTestProviderName => "Lokalny, duży (ok. 18 GB). Do testów, licencja niekomercyjna.",
            "DeepL" => "Szybki tłumacz w chmurze. Wymaga klucza API.",
            "Gemini" or "Claude" => "Model językowy w chmurze. Wymaga klucza API.",
            _ => "OpenAI, Ollama lub LM Studio. Lokalny serwer nie potrzebuje klucza."
        };
        ProviderBadgeText.Text = "Tłumacz: " + provider;
    }

    private async Task LoadRememberedApiKeyAsync()
    {
        var provider = ProviderComboBox.SelectedItem as string ?? string.Empty;
        var profile = ProviderUiProfile.For(provider);
        _loadingSecretUi = true;
        try
        {
            if (!profile.CanRememberApiKey)
            {
                RememberKeyCheckBox.IsChecked = false;
                return;
            }

            var key = await _services.ApiKeys.LoadAsync(provider);
            RememberKeyCheckBox.IsChecked = !string.IsNullOrWhiteSpace(key);
            ApiKeyTextBox.Text = key ?? string.Empty;
            ApiKeyHintText.Text = string.IsNullOrWhiteSpace(key)
                ? "Klucz nie jest zapisany. Zaznacz „Zapamiętaj”, aby zaszyfrować go dla bieżącego użytkownika Windows."
                : "Klucz wczytano z zaszyfrowanego magazynu użytkownika Windows.";
        }
        finally
        {
            _loadingSecretUi = false;
        }
    }

    private async void OnRememberKeyChanged(object? sender, RoutedEventArgs e)
    {
        if (_loadingSecretUi || _loadingSettings)
            return;

        var provider = ProviderComboBox.SelectedItem as string ?? string.Empty;
        if (!ProviderUiProfile.For(provider).CanRememberApiKey)
            return;

        if (RememberKeyCheckBox.IsChecked != true)
        {
            await _services.ApiKeys.RemoveAsync(provider);
            ApiKeyHintText.Text = "Klucz będzie używany tylko w tej sesji.";
        }
        else
        {
            ApiKeyHintText.Text = "Klucz zostanie zaszyfrowany dla bieżącego użytkownika Windows przy użyciu tłumaczenia.";
        }
    }

    private async void OnRememberApiKeyOnTranslate(object? sender, RoutedEventArgs e)
    {
        var provider = ProviderComboBox.SelectedItem as string ?? string.Empty;
        if (!ProviderUiProfile.For(provider).CanRememberApiKey)
            return;

        try
        {
            if (RememberKeyCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(ApiKeyTextBox.Text))
                await _services.ApiKeys.SaveAsync(provider, ApiKeyTextBox.Text!);
            else if (RememberKeyCheckBox.IsChecked != true)
                await _services.ApiKeys.RemoveAsync(provider);
        }
        catch (Exception ex)
        {
            SetStatus($"Nie udało się zapamiętać klucza API: {ex.Message}", StatusKind.Error);
        }
    }

    private async void OnArgosWindowClosed(object? sender, EventArgs e)
    {
        await NllbRuntimeRegistry.DisposeAsync();
    }
}
