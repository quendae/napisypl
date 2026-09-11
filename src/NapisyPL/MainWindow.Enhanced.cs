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

    private async void OnEnhancedChanged(object? sender, RoutedEventArgs e)
    {
        if (_pipeline is null)
            return;

        if (EnhancedCheckBox.IsChecked == true)
        {
            EnsureEnhancedConfigured();
            _pipeline.UseEnhanced = true;
            SetStatus(
                "Enhanced włączony — audio rozpozna rozmówców, a lokalny model sprawdzi tylko podejrzane formy po tłumaczeniu.",
                StatusKind.Normal);
        }
        else
        {
            _pipeline.UseEnhanced = false;
            if (_enhancedRuntimeManager is not null)
                await _enhancedRuntimeManager.DisposeAsync();
            SetStatus("Enhanced wyłączony — używany jest tryb Standard.", StatusKind.Normal);
        }

        RefreshReadyState();
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

        _enhancedHttpClient = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

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

        var runtimeOptions = LocalContextRuntimeOptions.CreateDefault();
        var runtimeAssets = new LocalContextAssetManager(_enhancedHttpClient, runtimeOptions);
        _enhancedRuntimeManager = new LocalContextRuntimeManager(_enhancedHttpClient, runtimeAssets, runtimeOptions);

        var targetedReview = new LocalTargetedGenderReviewService(
            _enhancedHttpClient,
            runtimeOptions.BaseUrl,
            "qwen3-1.7b",
            contextRadius: 2,
            logger: _appLogger,
            maxCandidatesPerBatch: 10,
            maxContextCuesPerBatch: 50);

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

        Closed += async (_, _) =>
        {
            if (_enhancedRuntimeManager is not null)
                await _enhancedRuntimeManager.DisposeAsync();
            _enhancedHttpClient?.Dispose();
            _enhancedHttpClient = null;
        };
    }
}
