using NapisyPL.Core.Diagnostics;
using NapisyPL.Core.Models;
using NapisyPL.Core.Translation;

namespace NapisyPL.Core.Services;

public sealed class FolderBatchService(
    FolderQueuePlanner planner,
    IMediaProbeService mediaProbe,
    ITranslationPipeline pipeline,
    IAppLogger logger)
{
    public async Task<FolderBatchResult> TranslateFolderAsync(
        string folder,
        ITranslationProvider provider,
        bool exportTxt,
        IProgress<FolderBatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var queue = planner.Plan(folder, exportTxt);
        var files = new List<FolderFileResult>(queue.Count);
        var translated = 0;
        var skipped = 0;
        var failed = 0;

        for (var offset = 0; offset < queue.Count; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = queue[offset];
            var fileIndex = offset + 1;
            var fileName = Path.GetFileName(item.InputPath);

            progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "queued"));

            if (item.SkipExisting)
            {
                skipped++;
                files.Add(new FolderFileResult(item.InputPath, FolderFileStatus.Skipped, item.ExpectedOutputPath, "output_exists"));
                logger.Info("folder_file_skip", ("file", fileName), ("reason", "output_exists"));
                progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "skipped"));
                continue;
            }

            try
            {
                SubtitleTrack? selectedTrack = null;
                if (TranslationPipeline.VideoExtensions.Contains(Path.GetExtension(item.InputPath)))
                {
                    progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "probing"));
                    var tracks = await mediaProbe.ProbeAsync(item.InputPath, cancellationToken: cancellationToken);
                    if (tracks.Count == 0)
                    {
                        skipped++;
                        files.Add(new FolderFileResult(item.InputPath, FolderFileStatus.Skipped, null, "no_subtitles"));
                        logger.Info("folder_file_skip", ("file", fileName), ("reason", "no_subtitles"));
                        progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "skipped"));
                        continue;
                    }

                    if (tracks.All(track => !track.IsText))
                    {
                        skipped++;
                        files.Add(new FolderFileResult(item.InputPath, FolderFileStatus.Skipped, null, "bitmap_only"));
                        logger.Info("folder_file_skip", ("file", fileName), ("reason", "bitmap_only"));
                        progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "skipped"));
                        continue;
                    }

                    selectedTrack = FfprobeParser.ChooseDefault(tracks);
                    if (selectedTrack is null || !selectedTrack.IsText)
                    {
                        skipped++;
                        files.Add(new FolderFileResult(item.InputPath, FolderFileStatus.Skipped, null, "no_text_subtitles"));
                        logger.Info("folder_file_skip", ("file", fileName), ("reason", "no_text_subtitles"));
                        progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "skipped"));
                        continue;
                    }
                }

                var translationProgress = new InlineProgress<TranslationProgress>(value =>
                    progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, value, "translating")));

                progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "translating"));
                var result = await pipeline.TranslateAsync(
                    item.InputPath,
                    selectedTrack,
                    provider,
                    exportTxt,
                    translationProgress,
                    cancellationToken: cancellationToken);

                translated++;
                files.Add(new FolderFileResult(item.InputPath, FolderFileStatus.Translated, result.PrimaryOutputPath));
                logger.Info("folder_file_done", ("file", fileName), ("result", "translated"));
                progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "completed"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                files.Add(new FolderFileResult(item.InputPath, FolderFileStatus.Failed, null, ex.GetType().Name));
                logger.Error("folder_file_failed", ("file", fileName), ("category", ex.GetType().Name), ("reason", "translation_failed"));
                progress?.Report(new FolderBatchProgress(fileIndex, queue.Count, fileName, null, "failed"));
            }
        }

        return new FolderBatchResult(translated, skipped, failed, files);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
