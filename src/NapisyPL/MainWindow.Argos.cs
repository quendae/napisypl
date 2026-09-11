using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.Security;
using NapisyPL.Core.Translation;

namespace NapisyPL;

public partial class MainWindow
{
    private const string ArgosProviderName = "Local Argos (offline)";
    private readonly ApiKeyStore _apiKeyStore = new();
    private bool _modernUiHooked;
    private bool _loadingSecretUi;

    private void OnArgosWindowOpened(object? sender, EventArgs e)
    {
        var items = ProviderNames.Contains(ArgosProviderName, StringComparer.Ordinal)
            ? ProviderNames
            : ProviderNames.Append(ArgosProviderName).ToArray();
        ProviderComboBox.ItemsSource = items;

        if (!_modernUiHooked)
        {
            ProviderComboBox.SelectionChanged += OnModernProviderSelectionChanged;
            TranslateButton.Click += OnRememberApiKeyOnTranslate;
            _modernUiHooked = true;
        }

        // The legacy OnOpened handler runs in the same event and restores ordinary settings.
        // Post our provider-specific visibility and encrypted secret restore afterwards.
        Dispatcher.UIThread.Post(async () =>
        {
            var settings = await _settingsStore.LoadAsync();
            if (string.Equals(settings.Provider, ArgosProviderName, StringComparison.Ordinal))
                ProviderComboBox.SelectedItem = ArgosProviderName;

            ApplyModernProviderUi();
            await LoadRememberedApiKeyAsync();
            RefreshReadyState();
        }, DispatcherPriority.Background);
    }

    private async void OnModernProviderSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingSettings)
            return;

        // Existing SelectionChanged logic applies defaults first. We only decide what the user
        // actually needs to see and then restore the provider-specific remembered secret.
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

        switch (provider)
        {
            case ArgosProviderName:
            {
                var runtime = ArgosRuntimeRegistry.GetOrCreate(_httpClient);
                runtime.StatusProgress = new Progress<string>(message => SetStatus(message, StatusKind.Normal));
                ApplyArgosProviderUi();
                ProviderHintText.Text = "Szybkie tłumaczenie lokalne przez Argos/CTranslate2. Bez API i bez wysyłania napisów do chmury.";
                break;
            }
            case "Local Qwen (offline)":
                ProviderHintText.Text = "Pełne tłumaczenie przez Qwen 1.7B — wolne i eksperymentalne. Enhanced używa Qwena osobno tylko jako korektora.";
                break;
            case "DeepL":
                ProviderHintText.Text = "Szybki translator chmurowy. Enhanced może później lokalnie poprawić rodzaj/liczbę przez Qwen 1.7B.";
                break;
            case "Gemini":
                ProviderHintText.Text = "Tłumaczenie LLM przez Gemini; model i klucz API są konfigurowalne.";
                break;
            case "Claude":
                ProviderHintText.Text = "Tłumaczenie LLM przez Claude; model i klucz API są konfigurowalne.";
                break;
            default:
                ProviderHintText.Text = "OpenAI-compatible: OpenAI, Ollama lub LM Studio. Klucz może być pusty dla lokalnego serwera.";
                break;
        }
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

            var key = await _apiKeyStore.LoadAsync(provider);
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
            await _apiKeyStore.RemoveAsync(provider);
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
                await _apiKeyStore.SaveAsync(provider, ApiKeyTextBox.Text!);
            else if (RememberKeyCheckBox.IsChecked != true)
                await _apiKeyStore.RemoveAsync(provider);
        }
        catch (Exception ex)
        {
            SetStatus($"Nie udało się zapamiętać klucza API: {ex.Message}", StatusKind.Error);
        }
    }

    private async void OnArgosWindowClosed(object? sender, EventArgs e)
    {
        await ArgosRuntimeRegistry.DisposeAsync();
    }
}
