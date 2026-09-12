using Avalonia.Controls;
using Avalonia.Interactivity;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL;

public partial class MainWindow
{
    private HttpClient? _enhancedHttpClient;
    private LocalContextRuntimeManager? _enhancedRuntimeManager;
    private bool _enhancedConfigured;
    private bool _enhancedCloseHooked;
    private bool _enhancedControlsReady;

    private async void OnEnhancedChanged(object? sender, RoutedEventArgs e)
    {
        if (_pipeline is null)
            return;

        if (EnhancedCheckBox.IsChecked == true)
        {
            SyncLocalProviderModelWithEnhanced();
            EnsureEnhancedConfigured();
            _pipeline.UseEnhanced = true;
            SetStatus(
                $"Enhanced włączony — {GetSelectedEnhancedOptions().ModelDisplayName}, backend {GetSelectedBackendLabel()}.",
                StatusKind.Normal);
        }
        else
        {
            _pipeline.UseEnhanced = false;
            await ResetEnhancedConfigurationAsync();
            if (ProviderComboBox.SelectedItem as string == "Local Qwen (offline)")
                ModelTextBox.Text = "qwen3-1.7b";
            SetStatus("Enhanced wyłączony — używany jest tryb Standard.", StatusKind.Normal);
        }

        RefreshReadyState();
    }

    private async void OnEnhancedConfigChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_enhancedControlsReady)
            return;

        UpdateEnhancedHint();
        SyncLocalProviderModelWithEnhanced();
        if (_pipeline is null || _busy || !_enhancedConfigured)
            return;

        var enabled = EnhancedCheckBox.IsChecked == true;
        await ResetEnhancedConfigurationAsync();
        if (enabled)
        {
            EnsureEnhancedConfigured();
            _pipeline.UseEnhanced = true;
            SetStatus(
                $"Enhanced: wybrano {GetSelectedEnhancedOptions().ModelDisplayName} · {GetSelectedBackendLabel()}.",
                StatusKind.Normal);
        }
    }

    private void CompleteEnhancedControlInitialization()
    {
        _enhancedControlsReady = true;
        UpdateEnhancedHint();
    }

    private async Task EnsureLocalQwenRunningAsync(CancellationToken cancellationToken)
    {
        if (EnhancedCheckBox.IsChecked != true)
        {
            if (EnhancedModelComboBox.SelectedIndex != 2)
                EnhancedModelComboBox.SelectedIndex = 2;
            ModelTextBox.Text = "qwen3-1.7b";
        }
        else
        {
            SyncLocalProviderModelWithEnhanced();
        }

        EnsureEnhancedConfigured();
        await _enhancedRuntimeManager!.EnsureRunningAsync(
            new Progress<string>(message => SetStatus(message, StatusKind.Normal)),
            cancellationToken);
    }

    private void EnsureEnhancedConfigured()
    {
        if (_enhancedConfigured)
            return;

        _enhancedHttpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var processRunner = new ProcessRunner();
        var ffmpegManager = new FfmpegManager(_httpClient);
        var subtitleExtraction = new SubtitleExtractionService(ffmpegManager, processRunner);
        var audioExtraction = new AudioContextExtractionService(ffmpegManager, processRunner);

        var diarizationOptions = SpeakerDiarizationOptions.CreateDefault();
        var diarizationAssets = new SpeakerDiarizationAssetManager(_enhancedHttpClient, diarizationOptions);
        var diarization = new SpeakerDiarizationService(diarizationAssets, diarizationOptions);
        var diarizationCache = SpeakerDiarizationCache.CreateDefault();

        var genderOptions = SpeakerVoiceGenderOptions.CreateDefault();
        var genderAssets = new SpeakerVoiceGenderAssetManager(_enhancedHttpClient, genderOptions);
        var voiceGender = new SpeakerVoiceGenderService(genderAssets, genderOptions, _appLogger);

        var speakerAnalysis = new SpeakerDiarizationAnalysisService(
            audioExtraction,
            diarization,
            _appLogger,
            diarizationCache,
            voiceGender);

        var runtimeOptions = GetSelectedEnhancedOptions();
        var runtimeAssets = new LocalContextAssetManager(_enhancedHttpClient, runtimeOptions);
        _enhancedRuntimeManager = new LocalContextRuntimeManager(
            _enhancedHttpClient,
            runtimeAssets,
            runtimeOptions,
            _appLogger);

        var targetedReview = new LocalTargetedGenderReviewService(
            _enhancedHttpClient,
            runtimeOptions.BaseUrl,
            runtimeOptions.ModelAlias,
            contextRadius: 2,
            logger: _appLogger);

        var enhancedPipeline = new EnhancedTranslationPipeline(
            _pipeline,
            subtitleExtraction,
            new SrtParser(),
            new SubtitleWriter(),
            new TranslationCoordinator(),
            speakerAnalysis,
            _enhancedRuntimeManager,
            targetedReview,
            _appLogger);

        _pipeline.EnhancedPipeline = enhancedPipeline;
        _enhancedConfigured = true;
        UpdateEnhancedHint();

        if (!_enhancedCloseHooked)
        {
            _enhancedCloseHooked = true;
            Closed += async (_, _) =>
            {
                if (_enhancedRuntimeManager is not null)
                    await _enhancedRuntimeManager.DisposeAsync();
                _enhancedHttpClient?.Dispose();
                _enhancedHttpClient = null;
            };
        }
    }

    private async Task ResetEnhancedConfigurationAsync()
    {
        if (_enhancedRuntimeManager is not null)
            await _enhancedRuntimeManager.DisposeAsync();
        _enhancedRuntimeManager = null;
        _enhancedConfigured = false;
    }

    private LocalContextRuntimeOptions GetSelectedEnhancedOptions()
    {
        var backend = EnhancedBackendComboBox.SelectedIndex switch
        {
            1 => LocalContextBackend.Vulkan,
            2 => LocalContextBackend.Cpu,
            _ => LocalContextBackend.Auto
        };
        var model = EnhancedModelComboBox.SelectedIndex switch
        {
            1 => LocalContextModelKind.GptOss20B,
            2 => LocalContextModelKind.Qwen3_1_7B,
            _ => LocalContextModelKind.Qwen35_9B
        };
        return LocalContextRuntimeOptions.Create(backend, model);
    }

    private string GetSelectedBackendLabel() => EnhancedBackendComboBox.SelectedIndex switch
    {
        1 => "Vulkan",
        2 => "CPU",
        _ => "Auto (Vulkan → CPU)"
    };

    private void SyncLocalProviderModelWithEnhanced()
    {
        if (ProviderComboBox.SelectedItem as string != "Local Qwen (offline)" || EnhancedCheckBox.IsChecked != true)
            return;

        var options = GetSelectedEnhancedOptions();
        ModelTextBox.Text = options.ModelAlias;
        BaseUrlTextBox.Text = options.BaseUrl;
    }

    private void UpdateEnhancedHint()
    {
        if (!_enhancedControlsReady ||
            EnhancedModelComboBox is null ||
            EnhancedBackendComboBox is null ||
            EnhancedModelHintText is null)
            return;

        var options = GetSelectedEnhancedOptions();
        EnhancedModelHintText.Text =
            $"Korektor: {options.ModelDisplayName} · {options.ModelApproxSize}. " +
            $"Backend: {GetSelectedBackendLabel()}. Model jest pobierany tylko przy pierwszym użyciu. " +
            "Enhanced używa też lokalnego modelu audio ~27 MB do ostrożnej klasyfikacji głosu rozmówców.";
    }
}
