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

    private async void OnEnhancedChanged(object? sender, RoutedEventArgs e)
    {
        if (_pipeline is null)
            return;

        if (EnhancedCheckBox.IsChecked == true)
        {
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
            SetStatus("Enhanced wyłączony — używany jest tryb Standard.", StatusKind.Normal);
        }

        RefreshReadyState();
    }

    private async void OnEnhancedConfigChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateEnhancedHint();
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

    private async Task EnsureLocalQwenRunningAsync(CancellationToken cancellationToken)
    {
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
        var speakerAnalysis = new SpeakerDiarizationAnalysisService(
            audioExtraction,
            diarization,
            _appLogger,
            diarizationCache);

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

    private void UpdateEnhancedHint()
    {
        if (EnhancedModelHintText is null)
            return;
        var options = GetSelectedEnhancedOptions();
        EnhancedModelHintText.Text =
            $"Korektor: {options.ModelDisplayName} · {options.ModelApproxSize}. " +
            $"Backend: {GetSelectedBackendLabel()}. Model jest pobierany tylko przy pierwszym użyciu.";
    }
}
