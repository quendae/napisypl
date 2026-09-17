using NapisyPL.Core.ContextResolution;
using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Security;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;
using NapisyPL.Core.Subtitles.Online;
using NapisyPL.Core.Translation;
using NapisyPL.Settings;

namespace NapisyPL;

/// <summary>
/// Services shared by the main window, the search window and the tray. There is one
/// log, one GPU model and one translation at a time, whichever window started it.
/// </summary>
public sealed class AppServices : IDisposable
{
    private static readonly Lazy<AppServices> SharedInstance = new(() => new AppServices());
    private HttpClient? _audioHttpClient;

    private AppServices()
    {
        var processRunner = new ProcessRunner();
        var ffmpegManager = new FfmpegManager(HttpClient);
        MediaProbe = new MediaProbeService(ffmpegManager, processRunner);
        SubtitleExtraction = new SubtitleExtractionService(ffmpegManager, processRunner);
        var parser = new SrtParser();
        Pipeline = new TranslationPipeline(parser, new SubtitleWriter(), SubtitleExtraction, new TranslationCoordinator());
        Qnapi = new QnapiSubtitleDownloader(new QnapiRuntimeManager(HttpClient), parser);
        SubtitlePipeline = new SubtitleAcquisitionPipeline(
            Pipeline,
            Qnapi,
            new SubtitleCueReader(SubtitleExtraction, parser),
            new SubtitleSynchronizationService());
        SubtitlePipeline.SpeechTimeline = LoadSpeechTimelineAsync;
        FolderBatch = new FolderBatchService(FolderQueuePlanner, MediaProbe, SubtitlePipeline, Logger);
        _ffmpegManager = ffmpegManager;
        _processRunner = processRunner;
    }

    private readonly FfmpegManager _ffmpegManager;
    private readonly ProcessRunner _processRunner;

    public static AppServices Shared => SharedInstance.Value;

    public HttpClient HttpClient { get; } = new() { Timeout = TimeSpan.FromMinutes(15) };
    public AppLogger Logger { get; } = new();
    public SettingsStore Settings { get; } = new();
    public ApiKeyStore ApiKeys { get; } = new();
    public FolderQueuePlanner FolderQueuePlanner { get; } = new();
    public MediaProbeService MediaProbe { get; }
    public SubtitleExtractionService SubtitleExtraction { get; }
    public TranslationPipeline Pipeline { get; }
    public QnapiSubtitleDownloader Qnapi { get; }
    public SubtitleAcquisitionPipeline SubtitlePipeline { get; }
    public FolderBatchService FolderBatch { get; }
    public EnhancedTranslationPipeline? EnhancedPipeline { get; private set; }

    /// <summary>Only one translation runs at a time: they share the GPU model.</summary>
    public SemaphoreSlim TranslationGate { get; } = new(1, 1);

    /// <summary>Applies the gender-correction options to the shared translation pipeline.</summary>
    public void ApplyGenderCorrection(bool enabled, bool strictVoiceEvidence)
    {
        if (enabled)
        {
            EnsureEnhancedPipeline();
            EnhancedPipeline!.HardVoiceTurnOnly = strictVoiceEvidence;
        }

        Pipeline.UseEnhanced = enabled;
    }

    /// <summary>The translator chosen in the main window, with its remembered API key.</summary>
    public async Task<ITranslationProvider> CreateProviderFromSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = await Settings.LoadAsync();
        var providerName = string.IsNullOrWhiteSpace(settings.Provider) ? AppSettings.DefaultProvider : settings.Provider;
        string apiKey;
        try
        {
            apiKey = await ApiKeys.LoadAsync(providerName, cancellationToken) ?? string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            apiKey = string.Empty;
        }

        return new DeferredTranslationProvider(() => ProviderFactory.Create(
            HttpClient, providerName, apiKey, settings.Model, settings.BaseUrl, Logger));
    }

    /// <summary>SubDL and OpenSubtitles.com join the Polish search only with the user's own key.</summary>
    public async Task RefreshOnlineSourcesAsync(CancellationToken cancellationToken = default)
    {
        var sources = new List<IOnlineSubtitleSource>();
        if (await LoadKeyAsync(SubDlSource.SourceName, cancellationToken) is { } subDlKey)
            sources.Add(new SubDlSource(HttpClient, subDlKey));
        if (await LoadKeyAsync(OpenSubtitlesComSource.SourceName, cancellationToken) is { } openSubtitlesKey)
            sources.Add(new OpenSubtitlesComSource(HttpClient, openSubtitlesKey, UserAgent));
        SubtitlePipeline.OnlineSources = sources;
    }

    public static string UserAgent =>
        "SubFlow v" + (typeof(AppServices).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");

    private async Task<string?> LoadKeyAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            var key = await ApiKeys.LoadAsync(name, cancellationToken);
            return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// When people speak: the cached diarization when there is one, otherwise Silero VAD over
    /// the dialogue channel. Both were calibrated on real episodes; a loudness detector was not
    /// reliable enough under music.
    /// </summary>
    private async Task<IReadOnlyList<SpeechSpan>?> LoadSpeechTimelineAsync(
        string videoPath,
        IProgress<string>? status,
        CancellationToken cancellationToken)
    {
        var cached = await SpeakerDiarizationCache.CreateDefault(SpeakerDiarizationOptions.CreateDefault())
            .TryLoadAsync(videoPath, cancellationToken);
        if (cached is { Count: > 0 })
        {
            return cached
                .Select(segment => new SpeechSpan(TimeSpan.FromSeconds(segment.StartSeconds), TimeSpan.FromSeconds(segment.EndSeconds)))
                .OrderBy(span => span.Start)
                .ToArray();
        }

        _audioHttpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var model = await new SpeakerDiarizationAssetManager(_audioHttpClient, SpeakerDiarizationOptions.CreateDefault())
            .EnsureSileroVadAsync(status, cancellationToken);
        var extraction = new AudioContextExtractionService(_ffmpegManager, _processRunner);
        var wavePath = await extraction.ExtractTemporaryMono16KhzWaveAsync(videoPath, status, cancellationToken);
        try
        {
            status?.Report("Sprawdzam, kiedy w filmie ktoś mówi…");
            return await Task.Run(() => SileroSpeechDetector.Detect(PcmWaveReader.ReadMono16(wavePath), model), cancellationToken);
        }
        finally
        {
            try { File.Delete(wavePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private void EnsureEnhancedPipeline()
    {
        if (EnhancedPipeline is not null)
            return;

        _audioHttpClient ??= new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var diarizationOptions = SpeakerDiarizationOptions.CreateDefault();
        var speakerAnalysis = new SpeakerDiarizationAnalysisService(
            new AudioContextExtractionService(_ffmpegManager, _processRunner),
            new SpeakerDiarizationService(new SpeakerDiarizationAssetManager(_audioHttpClient, diarizationOptions), diarizationOptions),
            Logger,
            SpeakerDiarizationCache.CreateDefault(diarizationOptions),
            new SpeakerVoiceGenderService(Logger),
            new CueVoiceGenderService(Logger));

        EnhancedPipeline = new EnhancedTranslationPipeline(
            Pipeline,
            SubtitleExtraction,
            new SrtParser(),
            new SubtitleWriter(),
            new TranslationCoordinator(),
            speakerAnalysis,
            new DeterministicGenderReviewService(),
            Logger);
        Pipeline.EnhancedPipeline = EnhancedPipeline;
    }

    public void Dispose()
    {
        Logger.Flush();
        _audioHttpClient?.Dispose();
        HttpClient.Dispose();
        Logger.Dispose();
        TranslationGate.Dispose();
    }
}
