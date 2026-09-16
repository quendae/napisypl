using System.Diagnostics;
using System.Text;
using NapisyPL.Core.Services;
using NapisyPL.Core.Subtitles;

namespace NapisyPL.Core.Tests;

public sealed class QnapiSubtitleDownloaderTests
{
    [Fact]
    public async Task DownloadInteractiveAsync_ShowsQnapiAndParsesOnlyItsOwnedSidecar()
    {
        using var fixture = new QnapiFixture();
        ProcessStartInfo? observed = null;
        string? output = null;
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                observed = startInfo;
                output = Path.ChangeExtension(fixture.VideoPath, ArgumentValue(startInfo.ArgumentList, "-e"));
                await File.WriteAllTextAsync(output,
                    "1\n00:00:01,000 --> 00:00:02,500\nWybrany napis.\n",
                    new UTF8Encoding(false), cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            });

        var result = await ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
            fixture.VideoPath, SubtitleLanguage.Polish);

        Assert.NotNull(result);
        Assert.Equal(SubtitleLanguage.Polish, result.Language);
        Assert.Equal("Wybrany napis.", result.Cues.Single().Text);
        Assert.NotNull(observed);
        Assert.DoesNotContain("-q", observed.ArgumentList);
        Assert.False(observed.CreateNoWindow);
        Assert.Equal("pl", ArgumentValue(observed.ArgumentList, "-l"));
        Assert.Equal("pl", ArgumentValue(observed.ArgumentList, "-lb"));
        Assert.NotNull(output);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task DownloadInteractiveAsync_DeletesSidecarWhenCancelled()
    {
        using var fixture = new QnapiFixture();
        string? output = null;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                output = Path.ChangeExtension(fixture.VideoPath, ArgumentValue(startInfo.ArgumentList, "-e"));
                await File.WriteAllTextAsync(output, "1\n00:00:01,000 --> 00:00:02,000\nNapis.\n", cancellationToken);
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            });
        using var cancellation = new CancellationTokenSource();
        var operation = ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
            fixture.VideoPath, SubtitleLanguage.Polish, cancellationToken: cancellation.Token);
        await entered.Task;
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);

        Assert.NotNull(output);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task DownloadInteractiveAsync_DeletesMalformedSidecar()
    {
        using var fixture = new QnapiFixture();
        string? output = null;
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                output = Path.ChangeExtension(fixture.VideoPath, ArgumentValue(startInfo.ArgumentList, "-e"));
                await File.WriteAllTextAsync(output, "not an srt", cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
                fixture.VideoPath, SubtitleLanguage.Polish));

        Assert.NotNull(output);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task DownloadInteractiveAsync_ReturnsNullWhenNoSelectionCreatesSidecar()
    {
        using var fixture = new QnapiFixture();
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            (_, _) => Task.FromResult(new QnapiProcessResult(0, string.Empty, string.Empty)));

        var result = await ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
            fixture.VideoPath, SubtitleLanguage.Polish);

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadInteractiveAsync_ReturnsNullWhenQnapiReportsNoSubtitles()
    {
        using var fixture = new QnapiFixture();
        var messages = new List<string>();
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            (_, _) => Task.FromResult(new QnapiProcessResult(6, string.Empty, string.Empty)));

        var result = await ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
            fixture.VideoPath, SubtitleLanguage.Polish, new SynchronousProgress(messages));

        Assert.Null(result);
        Assert.Contains("QNapi nie znalazło polskich napisów (kod 6 — brak wyników).", messages);
    }

    [Fact]
    public async Task DownloadInteractiveAsync_ParsesOwnedSidecarEvenWhenExitCodeIsSix()
    {
        using var fixture = new QnapiFixture();
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                var output = Path.ChangeExtension(fixture.VideoPath, ArgumentValue(startInfo.ArgumentList, "-e"));
                await File.WriteAllTextAsync(
                    output,
                    "1\n00:00:01,000 --> 00:00:02,000\nWybrane napisy.\n",
                    cancellationToken);
                return new QnapiProcessResult(6, string.Empty, string.Empty);
            });

        var result = await ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
            fixture.VideoPath, SubtitleLanguage.Polish);

        Assert.NotNull(result);
        Assert.Equal("Wybrane napisy.", result.Cues.Single().Text);
    }

    [Fact]
    public async Task DownloadInteractiveAsync_UsesLongerTimeoutThanAutomaticSearch()
    {
        using var fixture = new QnapiFixture();
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(30), cancellationToken);
                var output = Path.ChangeExtension(fixture.VideoPath, ArgumentValue(startInfo.ArgumentList, "-e"));
                await File.WriteAllTextAsync(output, "1\n00:00:01,000 --> 00:00:02,000\nNapis.\n", cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            },
            TimeSpan.FromMilliseconds(1));

        var result = await ((IInteractiveSubtitleDownloader)downloader).DownloadInteractiveAsync(
            fixture.VideoPath, SubtitleLanguage.Polish);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task DownloadAsync_UsesIsolatedSameLanguageArgumentsAndParsesSrt()
    {
        using var fixture = new QnapiFixture();
        ProcessStartInfo? observed = null;
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                observed = startInfo;
                var extension = ArgumentValue(startInfo.ArgumentList, "-e");
                var output = Path.ChangeExtension(fixture.VideoPath, extension);
                await File.WriteAllTextAsync(output,
                    "1\n00:00:01,000 --> 00:00:02,500\nHello!\n",
                    new UTF8Encoding(false), cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            });

        var result = await downloader.DownloadAsync(fixture.VideoPath, SubtitleLanguage.English);

        Assert.NotNull(result);
        Assert.Equal(SubtitleLanguage.English, result.Language);
        Assert.Equal("QNapi", result.Provider);
        Assert.Single(result.Cues);
        Assert.Equal("Hello!", result.Cues[0].Text);
        Assert.NotNull(observed);
        Assert.Equal(fixture.Runtime.ExecutablePath, observed.FileName);
        Assert.Equal("en", ArgumentValue(observed.ArgumentList, "-l"));
        Assert.Equal("en", ArgumentValue(observed.ArgumentList, "-lb"));
        Assert.Equal("SRT", ArgumentValue(observed.ArgumentList, "-f"));
        Assert.Contains("-q", observed.ArgumentList);
        Assert.Contains("-d", observed.ArgumentList);
        Assert.Equal(fixture.VideoPath, observed.ArgumentList[^1]);
        Assert.StartsWith("npl", ArgumentValue(observed.ArgumentList, "-e"), StringComparison.Ordinal);
        Assert.DoesNotContain(' ', ArgumentValue(observed.ArgumentList, "-e"));
    }

    [Fact]
    public async Task DownloadAsync_ReturnsNullWhenQnapiProducesNoOutput()
    {
        using var fixture = new QnapiFixture();
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            (_, _) => Task.FromResult(new QnapiProcessResult(0, string.Empty, string.Empty)));

        var result = await downloader.DownloadAsync(fixture.VideoPath, SubtitleLanguage.Polish);

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadAsync_DeletesTemporarySidecarAfterMalformedOutput()
    {
        using var fixture = new QnapiFixture();
        string? output = null;
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (startInfo, cancellationToken) =>
            {
                output = Path.ChangeExtension(fixture.VideoPath, ArgumentValue(startInfo.ArgumentList, "-e"));
                await File.WriteAllTextAsync(output, "not an srt", cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            });

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(fixture.VideoPath, SubtitleLanguage.Polish));

        Assert.NotNull(output);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task DownloadAsync_ReportsNonzeroProcessFailureWithoutOutput()
    {
        using var fixture = new QnapiFixture();
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            (_, _) => Task.FromResult(new QnapiProcessResult(-1, string.Empty, "argument parsing failed")));

        var error = await Assert.ThrowsAsync<IOException>(() =>
            downloader.DownloadAsync(fixture.VideoPath, SubtitleLanguage.Polish));

        Assert.Contains("argument parsing failed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DownloadAsync_PassesCancellationToProcessInvoker()
    {
        using var fixture = new QnapiFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var downloader = new QnapiSubtitleDownloader(
            fixture.Runtime,
            new SrtParser(),
            async (_, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new QnapiProcessResult(0, string.Empty, string.Empty);
            },
            TimeSpan.FromMinutes(1));
        using var cts = new CancellationTokenSource();

        var operation = downloader.DownloadAsync(
            fixture.VideoPath, SubtitleLanguage.Polish, cancellationToken: cts.Token);
        await entered.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
    }

    private static string ArgumentValue(IList<string> arguments, string option)
    {
        var index = arguments.IndexOf(option);
        Assert.True(index >= 0 && index + 1 < arguments.Count, $"Missing value for {option}.");
        return arguments[index + 1];
    }

    private sealed class SynchronousProgress(List<string> messages) : IProgress<string>
    {
        public void Report(string value) => messages.Add(value);
    }

    private sealed class QnapiFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "NapisyPL-QnapiTests-" + Guid.NewGuid().ToString("N"));

        public QnapiFixture()
        {
            Directory.CreateDirectory(_root);
            VideoPath = Path.Combine(_root, "Movie.With.Dots.mkv");
            File.WriteAllBytes(VideoPath, [1, 2, 3]);
            var executable = Path.Combine(_root, "qnapi.exe");
            File.WriteAllBytes(executable, [1]);
            Runtime = new FakeQnapiRuntime(executable);
        }

        public string VideoPath { get; }
        public FakeQnapiRuntime Runtime { get; }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed class FakeQnapiRuntime(string executablePath) : IQnapiRuntime
    {
        public string ExecutablePath { get; } = executablePath;

        public Task<string> EnsureAvailableAsync(
            IProgress<string>? status = null,
            CancellationToken cancellationToken = default) => Task.FromResult(ExecutablePath);
    }
}
