using Avalonia.Interactivity;
using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Services;
using NapisyPL.Core.Translation;

namespace NapisyPL;

public partial class MainWindow
{
    private HttpClient? _enhancedAudioHttpClient;
    private HttpClient? _localQwenHttpClient;
    private LocalContextRuntimeManager? _localQwenRuntimeManager;
    private bool _enhancedConfigured;
    private bool _enhancedCloseHooked;

    private void OnEnhancedChanged(object? sender, RoutedEventArgs e)
    {
        if (_pipeline is null)
            return;

        if (EnhancedCheckBox.IsChecked == true)
        {
            EnsureEnhancedConfigured();
            _pipeline.UseEnhanced = true;
            SetStatus(
                "Enhanced włączony — audio i kolejność rozmówców będą użyte tylko do deterministycznej korekty rodzaju.",
                StatusKind.Normal);
        }
        else
        {
            _pipeline.UseEnhanced = false;
            SetStatus("Enhanced wyłączony — używany jest wynik tłumacza bazowego bez korekty audio.", StatusKind.Normal);
        }

        RefreshReadyState();
    }

    private void CompleteEnhancedControlInitialization()
    {
        // Kept as the constructor hook so older window initialization remains stable.
        // Enhanced now has no reviewer model/backend controls to initialize.
    }

    private async Task EnsureLocalQwenRunningAsync(CancellationToken cancellationToken)
    {
        _localQwenHttpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        if (_localQwenRuntimeManager is null)
        {
            var options = LocalContextRuntimeOptions.Create(
                LocalContextBackend.Auto,
                LocalContextModelKind.Qwen3_1_7B);
            var assets = new LocalContextAssetManager(_localQwenHttpClient, options);
            _localQwenRuntimeManager = new LocalContextRuntimeManager(
                _localQwenHttpClient,
                assets,
                options,
                _appLogger);
            ModelTextBox.Text = options.ModelAlias;
            BaseUrlTextBox.Text = options.BaseUrl;
        }

        await _localQwenRuntimeManager.EnsureRunningAsync(
            new Progress<string>(message => SetStatus(message, StatusKind.Normal)),
            cancellationToken);
    }

    private void EnsureEnhancedConfigured()
    {
        if (_enhancedConfigured)
            return;

        _enhancedAudioHttpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        var processRunner = new ProcessRunner();
        var ffmpegManager = new FfmpegManager(_httpClient);
        var subtitleExtraction = new SubtitleExtractionService(ffmpegManager, processRunner);
        var audioExtraction = new AudioContextExtractionService(ffmpegManager, processRunner);

        var diarizationOptions = SpeakerDiarizationOptions.CreateDefault();
        var diarizationAssets = new SpeakerDiarizationAssetManager(_enhancedAudioHttpClient, diarizationOptions);
        var diarization = new SpeakerDiarizationService(diarizationAssets, diarizationOptions);
        var diarizationCache = SpeakerDiarizationCache.CreateDefault();

        var genderOptions = SpeakerVoiceGenderOptions.CreateDefault();
        var genderAssets = new SpeakerVoiceGenderAssetManager(_enhancedAudioHttpClient, genderOptions);
        var voiceGender = new SpeakerVoiceGenderService(genderAssets, genderOptions, _appLogger);

        var speakerAnalysis = new SpeakerDiarizationAnalysisService(
            audioExtraction,
            diarization,
            _appLogger,
            diarizationCache,
            voiceGender);

        var enhancedPipeline = new EnhancedTranslationPipeline(
            _pipeline,
            subtitleExtraction,
            new SrtParser(),
            new SubtitleWriter(),
            new TranslationCoordinator(),
            speakerAnalysis,
            new DeterministicGenderReviewService(),
            _appLogger);

        _pipeline.EnhancedPipeline = enhancedPipeline;
        _enhancedConfigured = true;

        if (!_enhancedCloseHooked)
        {
            _enhancedCloseHooked = true;
            Closed += async (_, _) =>
            {
                if (_localQwenRuntimeManager is not null)
                    await _localQwenRuntimeManager.DisposeAsync();
                _localQwenRuntimeManager = null;
                _localQwenHttpClient?.Dispose();
                _localQwenHttpClient = null;
                _enhancedAudioHttpClient?.Dispose();
                _enhancedAudioHttpClient = null;
            };
        }
    }
}
