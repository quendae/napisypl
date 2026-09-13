# NapisyPL v2 Batch + Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add folder-wide sequential translation, visible batch/file progress, and privacy-safe diagnostic logs without changing the successful single-file workflow.

**Architecture:** Keep `TranslationPipeline` responsible for one input file and add a `FolderBatchService` above it. Replace the current scalar translation progress with a structured progress record so the UI can distinguish waiting-on-provider, completed batches, and completed segments; the UI owns a one-second elapsed timer while an HTTP/local request is in flight. Add a small `IAppLogger` abstraction in Core and a file implementation that records metadata/timings but never keys, headers, prompts, or subtitle text.

**Tech Stack:** .NET 10, C# 14, Avalonia 12, xUnit, existing FFmpeg/ffprobe services.

**Spec:** `docs/superpowers/specs/2026-09-10-napisypl-local-batch-design.md`

## Global Constraints

- Windows-first; existing Linux-friendly architecture must not be made Windows-only except where the existing FFmpeg acquisition already is.
- No Whisper/audio transcription and no OCR for bitmap subtitles.
- Folder scan is non-recursive in v2.
- Folder processing is sequential.
- Existing `.pl.srt` / `.pl.txt` outputs are skipped by default.
- A failure in one folder item must not abort the remaining queue.
- Never log API keys, authentication headers, full prompts, or subtitle text.
- Existing MVP tests remain green.

---

### Task 1: Structured translation progress

**Files:**
- Create: `src/NapisyPL.Core/Models/TranslationProgress.cs`
- Modify: `src/NapisyPL.Core/Translation/TranslationCoordinator.cs`
- Modify: `src/NapisyPL.Core/Services/TranslationPipeline.cs`
- Test: `tests/NapisyPL.Core.Tests/TranslationCoordinatorTests.cs`

**Interfaces:**
- Produces: `TranslationProgress(int CompletedSegments, int TotalSegments, int BatchIndex, int BatchCount, bool WaitingForProvider, DateTimeOffset BatchStartedAt)`.
- `TranslationCoordinator.TranslateCuesAsync(..., IProgress<TranslationProgress>? progress, ...)` and `TranslateTextLinesAsync` use the new type.

- [ ] **Step 1: Write the failing progress lifecycle test**

Add a fake provider that completes immediately and assert the coordinator reports `WaitingForProvider=true` before each request and `false` after it, with a stable `BatchStartedAt` for the pair.

```csharp
var reports = new List<TranslationProgress>();
await coordinator.TranslateCuesAsync(cues, provider, new Progress<TranslationProgress>(reports.Add));
Assert.Contains(reports, x => x.BatchIndex == 1 && x.WaitingForProvider);
Assert.Contains(reports, x => x.BatchIndex == 1 && !x.WaitingForProvider && x.CompletedSegments > 0);
```

- [ ] **Step 2: Run the targeted test and verify RED**

Run: `dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj -c Release --filter TranslationCoordinatorTests`

Expected: compile/test failure because `TranslationProgress` and the new progress signature do not exist.

- [ ] **Step 3: Add the progress record and pre-compute batches**

Create:

```csharp
namespace NapisyPL.Core.Models;

public sealed record TranslationProgress(
    int CompletedSegments,
    int TotalSegments,
    int BatchIndex,
    int BatchCount,
    bool WaitingForProvider,
    DateTimeOffset BatchStartedAt);
```

Materialize `BuildCueBatches(cues).ToArray()` / `BuildTextBatches(...).ToArray()` before iterating so `BatchCount` is known. Report once immediately before `provider.TranslateAsync` and once after applying the returned segments.

- [ ] **Step 4: Update pipeline signatures and run tests**

Run: `dotnet test NapisyPL.sln -c Release`

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/NapisyPL.Core tests/NapisyPL.Core.Tests
git commit -m "feat: report structured translation progress"
```

---

### Task 2: Provider-aware batch sizing

**Files:**
- Create: `src/NapisyPL.Core/Translation/TranslationBatchPolicy.cs`
- Modify: `src/NapisyPL.Core/Translation/ITranslationProvider.cs`
- Modify: `src/NapisyPL.Core/Translation/TranslationCoordinator.cs`
- Modify: `src/NapisyPL.Core/Translation/Providers/DeepLProvider.cs`
- Modify: `src/NapisyPL.Core/Translation/Providers/GeminiProvider.cs`
- Modify: `src/NapisyPL.Core/Translation/Providers/OpenAiCompatibleProvider.cs`
- Modify: `src/NapisyPL.Core/Translation/Providers/AnthropicProvider.cs`
- Test: `tests/NapisyPL.Core.Tests/TranslationCoordinatorTests.cs`

**Interfaces:**
- Produces: `TranslationBatchPolicy(int MaxSegments, int MaxCharacters)`.
- `ITranslationProvider.BatchPolicy` supplies policy to the coordinator.

- [ ] **Step 1: Add failing policy test**

Use a fake provider with `new TranslationBatchPolicy(2, 1000)` and five short cues; assert three provider calls are made.

- [ ] **Step 2: Run test and verify RED**

Run: `dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj -c Release --filter TranslationCoordinatorTests`

- [ ] **Step 3: Implement policy**

```csharp
public sealed record TranslationBatchPolicy(int MaxSegments, int MaxCharacters)
{
    public static TranslationBatchPolicy LlmDefault { get; } = new(20, 6000);
    public static TranslationBatchPolicy MachineTranslation { get; } = new(40, 12000);
}
```

Extend the provider interface:

```csharp
public interface ITranslationProvider
{
    string DisplayName { get; }
    TranslationBatchPolicy BatchPolicy { get; }
    Task<IReadOnlyDictionary<int,string>> TranslateAsync(...);
}
```

Set Gemini/OpenAI-compatible/Claude to `LlmDefault`, DeepL to `MachineTranslation`; coordinator uses `provider.BatchPolicy` instead of constants.

- [ ] **Step 4: Run full test suite**

Run: `dotnet test NapisyPL.sln -c Release`

- [ ] **Step 5: Commit**

```bash
git add src/NapisyPL.Core tests/NapisyPL.Core.Tests
git commit -m "feat: tune batching per translation provider"
```

---

### Task 3: Privacy-safe application logger

**Files:**
- Create: `src/NapisyPL.Core/Diagnostics/IAppLogger.cs`
- Create: `src/NapisyPL.Core/Diagnostics/AppLogger.cs`
- Create: `src/NapisyPL.Core/Diagnostics/NullAppLogger.cs`
- Test: `tests/NapisyPL.Core.Tests/AppLoggerTests.cs`

**Interfaces:**
- Produces: `IAppLogger.Info(string eventName, params (string Key, object? Value)[] fields)` and `Error(...)`.
- Produces: `AppLogger.LogPath`.

- [ ] **Step 1: Write failing secret-redaction test**

Create a logger rooted in a temp directory, log fields named `apiKey`, `authorization`, `x-api-key`, `prompt`, `subtitleText`, and a safe `batch=3`; assert the file contains `batch=3` but none of the supplied secret/text values.

- [ ] **Step 2: Run test and verify RED**

Run: `dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj -c Release --filter AppLoggerTests`

- [ ] **Step 3: Implement logger with an allow-list**

Do not attempt heuristic value scrubbing. Accept only known metadata keys such as `provider`, `model`, `file`, `segmentCount`, `characterCount`, `batch`, `elapsedMs`, `httpStatus`, `category`, `version`, `result`, `reason`; silently replace disallowed keys with `[REDACTED]`.

Default file: `%LocalAppData%/NapisyPL/logs/napisypl-YYYY-MM-DD.log`.

- [ ] **Step 4: Run tests**

Run: `dotnet test NapisyPL.sln -c Release`

- [ ] **Step 5: Commit**

```bash
git add src/NapisyPL.Core/Diagnostics tests/NapisyPL.Core.Tests/AppLoggerTests.cs
git commit -m "feat: add privacy-safe diagnostic logging"
```

---

### Task 4: Timed provider-call diagnostics

**Files:**
- Create: `src/NapisyPL.Core/Translation/LoggingTranslationProvider.cs`
- Test: `tests/NapisyPL.Core.Tests/LoggingTranslationProviderTests.cs`
- Modify: `src/NapisyPL/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `IAppLogger`, `ITranslationProvider`.
- Produces: a decorator implementing `ITranslationProvider` and forwarding `BatchPolicy`.

- [ ] **Step 1: Write failing decorator test**

Wrap a fake provider, translate two segments, and assert log entries contain provider name, segment count, character count, elapsed time, and result, while translated text itself is absent.

- [ ] **Step 2: Run test and verify RED**

- [ ] **Step 3: Implement decorator**

Use `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime(start)`. Log `translation_request_start`, `translation_request_end`, and `translation_request_error`; rethrow the original exception.

- [ ] **Step 4: Wrap providers at creation time in MainWindow**

Instantiate one `AppLogger` for the application and wrap the provider returned by `ProviderFactory.Create(...)` before passing it to the pipeline.

- [ ] **Step 5: Run full suite and commit**

Run: `dotnet test NapisyPL.sln -c Release`

```bash
git add src tests
git commit -m "feat: log translation request timings"
```

---

### Task 5: Folder queue planning

**Files:**
- Create: `src/NapisyPL.Core/Models/FolderQueueItem.cs`
- Create: `src/NapisyPL.Core/Services/FolderQueuePlanner.cs`
- Test: `tests/NapisyPL.Core.Tests/FolderQueuePlannerTests.cs`

**Interfaces:**
- Produces: `FolderQueueItem(string InputPath, string ExpectedOutputPath, bool SkipExisting)`.
- Produces: `IReadOnlyList<FolderQueueItem> FolderQueuePlanner.Plan(string folder, bool exportTxt)`.

- [ ] **Step 1: Write failing queue tests**

Cover: supported extensions only, non-recursive behavior, generated `*.pl.srt`/`*.pl.txt` exclusion, and existing expected output marked as skipped.

- [ ] **Step 2: Run and verify RED**

Run: `dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj -c Release --filter FolderQueuePlannerTests`

- [ ] **Step 3: Implement deterministic planner**

Sort input paths with `StringComparer.OrdinalIgnoreCase`; use the existing `TranslationPipeline.IsSupportedInput` extension logic. Never descend into subdirectories.

- [ ] **Step 4: Run full tests and commit**

```bash
git add src/NapisyPL.Core tests/NapisyPL.Core.Tests
git commit -m "feat: plan folder translation queues"
```

---

### Task 6: Sequential folder execution

**Files:**
- Create: `src/NapisyPL.Core/Models/FolderBatchProgress.cs`
- Create: `src/NapisyPL.Core/Models/FolderBatchResult.cs`
- Create: `src/NapisyPL.Core/Services/FolderBatchService.cs`
- Test: `tests/NapisyPL.Core.Tests/FolderBatchServiceTests.cs`

**Interfaces:**
- Produces: `FolderBatchProgress(int FileIndex, int FileCount, string FileName, TranslationProgress? Translation, string Stage)`.
- Produces: `FolderBatchResult(int Translated, int Skipped, int Failed, IReadOnlyList<FolderFileResult> Files)`.
- Consumes: `FolderQueuePlanner`, `MediaProbeService`, `TranslationPipeline`, `IAppLogger`.

- [ ] **Step 1: Write failing service tests using fakes**

Cover: sequential order, continue after one thrown exception, skip existing output, video with no subtitle tracks, video with bitmap-only tracks, automatic `FfprobeParser.ChooseDefault`, and summary counts.

- [ ] **Step 2: Run and verify RED**

- [ ] **Step 3: Implement one-file-at-a-time runner**

For subtitle inputs call `TranslationPipeline.TranslateAsync` directly. For videos probe first; if no text track exists return skipped reason instead of throwing. Catch non-cancellation exceptions per file, log them, append failed result, and continue. Allow `OperationCanceledException` to abort the whole batch.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test NapisyPL.sln -c Release`

```bash
git add src/NapisyPL.Core tests/NapisyPL.Core.Tests
git commit -m "feat: translate folders sequentially"
```

---

### Task 7: Folder and live progress UI

**Files:**
- Modify: `src/NapisyPL/MainWindow.axaml`
- Modify: `src/NapisyPL/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `FolderBatchService`, `FolderBatchProgress`, `TranslationProgress`, `AppLogger.LogPath`.

- [ ] **Step 1: Add UI controls without introducing another screen**

Add `Wybierz folder` beside `Wybierz plik`, a compact batch status panel (`Folder`, `Aktualnie`, `Segment`, `Partia · czas`) that is hidden for ordinary idle single-file mode, and `Otwórz log` beside the output-folder action.

- [ ] **Step 2: Add folder picker and folder drag/drop**

Use `StorageProvider.OpenFolderPickerAsync`. When dropped storage item is a directory, create a folder queue; otherwise keep current single-file behavior.

- [ ] **Step 3: Add a one-second elapsed timer**

Use `Avalonia.Threading.DispatcherTimer` only while `TranslationProgress.WaitingForProvider` is true. Render e.g. `Partia 10 / 19 · 00:07`. Do not fabricate percentage progress inside an HTTP request.

- [ ] **Step 4: Wire batch execution**

Disable input/provider controls while the queue runs; keep the existing Cancel button. At completion display `Przetłumaczone X · pominięte Y · błędy Z` and make `Otwórz log` available even if failures occurred.

- [ ] **Step 5: Compile/publish verification**

Run:

```bash
dotnet test NapisyPL.sln -c Release
dotnet publish src/NapisyPL/NapisyPL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Expected: all tests PASS; publish exit code 0.

- [ ] **Step 6: Commit**

```bash
git add src/NapisyPL
git commit -m "feat: add folder translation and live progress UI"
```

---

### Task 8: Final regression and documentation

**Files:**
- Modify: `README.md`
- Modify: `DESIGN.md`

- [ ] **Step 1: Document folder behavior, logs, and new batching**

State explicitly that folder scan is non-recursive, existing Polish outputs are skipped, and log files contain metadata/timings but not subtitle content or credentials.

- [ ] **Step 2: Run fresh full verification**

Run:

```bash
dotnet test NapisyPL.sln -c Release
dotnet publish src/NapisyPL/NapisyPL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

- [ ] **Step 3: Commit**

```bash
git add README.md DESIGN.md
git commit -m "docs: document NapisyPL v2 batch diagnostics"
```
