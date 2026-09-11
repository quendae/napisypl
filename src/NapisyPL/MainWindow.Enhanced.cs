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
        // XAML events may fire during InitializeComponent, before the main constructor wires _pipeline.
        if (_pipeline is null)
            return;

        if (EnhancedCheckBox.IsChecked == true)
        {
            EnsureEnhancedConfigured();
            _pipeline.UseEnhanced = true;
            SetStatus(
                "Enhanced włączony — dla filmów SubFlow przeanalizuje audio lokalnie przed tłumaczeniem.",
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

        var runtimeOptions = LocalContextRuntimeOptions.CreateDefault();
        var runtimeAssets = new LocalContextAssetManager(_enhancedHttpClient, runtimeOptions);
        _enhancedRuntimeManager = new LocalContextRuntimeManager(_enhancedHttpClient, runtimeAssets, runtimeOptions);

        var contextResolver = new OpenAiContextResolver(
            _enhancedHttpClient,
            runtimeOptions.BaseUrl,
            "qwen3-1.7b");
        var contextCoordinator = new ContextResolutionCoordinator(windowSize: 40, overlap: 10);
        var contextAnalysis = new EnhancedContextAnalysisService(
            audioExtraction,
            diarization,
            _enhancedRuntimeManager,
            contextResolver,
            contextCoordinator,
            _appLogger);
        var genderReview = new LocalGenderReviewService(
            _enhancedHttpClient,
            runtimeOptions.BaseUrl,
            "qwen3-1.7b",
            windowSize: 40);

        var enhancedPipeline = new EnhancedTranslationPipeline(
            _pipeline,
            subtitleExtraction,
            new SrtParser(),
            new SubtitleWriter(),
            new TranslationCoordinator(),
            contextAnalysis,
            _enhancedRuntimeManager,
            genderReview,
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
