using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using NapisyPL.Core.LocalTranslation;
using NapisyPL.Core.OfflineMt.Bergamot;
using NapisyPL.Core.OfflineMt.Nllb;
using NapisyPL.Core.Security;
using NapisyPL.Core.Translation;

namespace NapisyPL;

public partial class MainWindow
{
    private const string ArgosProviderName = "Local Argos (offline)";
    private const string FirefoxProviderName = "Firefox/Bergamot (offline)";
    private const string OpusProviderName = "OPUS-MT / Marian (offline)";
    private const string NllbFastProviderName = "NLLB 600M — Fast";
    private const string NllbBalancedProviderName = "NLLB 1.3B — Balanced";
    private const string NllbQualityTestProviderName = "NLLB 3.3B — Quality Test";
    private const string MadladQualityProviderName = "MADLAD-400 3B — Quality";
    private const string LegacyNllbProviderName = "NLLB-200 600M (offline, benchmark)";
    private static readonly string[] LocalMtProviderNames =
    [
        ArgosProviderName,
        FirefoxProviderName,
        OpusProviderName,
        NllbFastProviderName,
        NllbBalancedProviderName,
        NllbQualityTestProviderName,
        MadladQualityProviderName
    ];

    private readonly ApiKeyStore _apiKeyStore = new();
    private bool _modernUiHooked;
    private bool _loadingSecretUi;

    private void OnArgosWindowOpened(object? sender, EventArgs e)
    {
        var items = ProviderNames
            .Concat(LocalMtProviderNames)
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
            var restoredProvider = settings.Provider == LegacyNllbProviderName
                ? NllbFastProviderName
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
            case FirefoxProviderName:
                ProviderHintText.Text = "Firefox/Bergamot EN→PL — lokalny silnik Mozilli. Przy pierwszym użyciu pobiera model, później działa offline.";
                break;
            case OpusProviderName:
                ProviderHintText.Text = "OPUS-MT / Marian EN→PL — przypięty model 2021-02-19 do benchmarku. Przy pierwszym użyciu pobiera model, później działa offline.";
                break;
            case NllbFastProviderName:
                ProviderHintText.Text = "NLLB distilled 600M · Fast. Auto GPU/CPU; GPU używa FP16. CC-BY-NC-4.0 · benchmark/non-commercial. Model pozostaje w pamięci podczas całej kolejki folderu.";
                break;
            case NllbBalancedProviderName:
                ProviderHintText.Text = "NLLB distilled 1.3B · Balanced. Auto GPU/CPU; GPU używa FP16. CC-BY-NC-4.0 · benchmark/non-commercial. Model pozostaje w pamięci podczas całej kolejki folderu.";
                break;
            case NllbQualityTestProviderName:
                ProviderHintText.Text = "NLLB full 3.3B · Quality Test. Auto GPU/CPU; GPU używa FP16 i startuje od batch 4. CC-BY-NC-4.0 · benchmark/non-commercial. Duży model (~17.6 GB pobrania); GPU zdecydowanie zalecane. Model pozostaje w pamięci podczas całej kolejki folderu.";
                break;
            case MadladQualityProviderName:
                ProviderHintText.Text = "MADLAD-400 3B · Quality. Auto GPU/CPU; GPU używa FP16. Apache-2.0. Model pozostaje w pamięci podczas całej kolejki folderu.";
                break;
            case "Local Qwen (offline)":
                ProviderHintText.Text = "Pełne tłumaczenie przez Qwen 1.7B — wolne i eksperymentalne. Enhanced nie używa dodatkowego LLM do korekty.";
                break;
            case "DeepL":
                ProviderHintText.Text = "Szybki translator chmurowy. Enhanced może lokalnie skorygować pewne formy rodzaju na podstawie audio i kolejności rozmówców.";
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
        await NllbRuntimeRegistry.DisposeAsync();
        await OfflineMtRuntimeRegistry.DisposeAsync();
        await ArgosRuntimeRegistry.DisposeAsync();
    }
}
