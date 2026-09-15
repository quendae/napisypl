# Subtitle synchronization and alternatives implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Validate and safely synchronize downloaded Polish subtitles against embedded English timing, offer interactive QNapi or local SRT alternatives, and invoke machine translation only after Polish options are exhausted.

**Architecture:** Add a pure robust timeline analyzer in Core, then inject an embedded-reference reader into the existing acquisition pipeline. The concrete pipeline exposes a single-file interactive method while its existing interface method remains noninteractive by construction. Keep QNapi process details inside its adapter and implement the modal chooser in Avalonia; folder batches continue to the English fallback without access to modal interaction.

**Tech Stack:** .NET 10, C#, Avalonia, xUnit, FFmpeg, external QNapi executable.

**Spec:** `docs/superpowers/specs/2026-09-15-subtitle-sync-and-alternatives-design.md`

## Global Constraints

- Existing `<video>.pl.srt` files are never overwritten.
- Cue matching must not depend on equal cue counts, cue indexes, or translated text.
- Folder processing must never display a modal alternative-selection dialog.
- Machine translation runs only after all usable Polish sources are exhausted.
- Local SRT inputs remain untouched; QNapi temporary sidecars are always deleted.
- Gender resolution, translation-provider behavior, and MadLad configuration remain unchanged.
- Every production behavior is introduced through a failing xUnit test first.

---

### Task 1: Robust subtitle timing analysis and transformation

**Files:**
- Create: `src/NapisyPL.Core/Subtitles/SubtitleSynchronizationService.cs`
- Create: `tests/NapisyPL.Core.Tests/SubtitleSynchronizationServiceTests.cs`

**Interfaces:**
- Produces: `SubtitleSyncDecision`, `SubtitleTimeTransform`, `SubtitleTimingAnalysis`, `SubtitleSynchronizationService.Analyze(reference, candidate)`, and `SubtitleSynchronizationService.Apply(candidate, transform)`.
- Consumes: `IReadOnlyList<SubtitleCue>` from the existing model.

- [ ] **Step 1: Write failing tests for aligned but differently segmented cues**

Create cues with 10 reference entries and 8 candidates whose timestamps differ by 100 ms and whose first/last dialogue boundaries still align. Assert `Aligned`, scale near `1`, offset near `-100 ms`, coverage at least `0.75`, and verify that differing indexes and counts do not prevent matching.

- [ ] **Step 2: Run the focused tests and verify RED**

Run: `.run/dotnet/dotnet.exe test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj --filter FullyQualifiedName~SubtitleSynchronizationServiceTests`

Expected: compilation fails because `SubtitleSynchronizationService` and result types do not exist.

- [ ] **Step 3: Implement aligned analysis with robust nearest-boundary matching**

Create immutable result records and a pure service. Validate non-empty ordered cues, collect start/end boundaries, evaluate scale `1.0`, calculate the median nearest-reference offset, retain residuals within 1 second, and calculate coverage, median, and P90. Return `Aligned` only at the spec thresholds.

- [ ] **Step 4: Add failing tests for FPS transforms, fixed offsets, rejection, and credits**

Cover a `25/23.976` scale, a two-second fixed shift, unrelated timelines, negative transformed starts, and two trailing provider-credit cues. Assert safe synchronization for the first two, rejection for unrelated data, valid clamped output, and unchanged confidence when credits are appended.

- [ ] **Step 5: Run focused tests and verify RED for the new decisions**

Run the Task 1 filter and confirm at least the FPS or rejection case fails for the missing behavior.

- [ ] **Step 6: Implement bounded affine/FPS candidate evaluation and Apply**

Evaluate `1.0`, `24/25`, `23.976/25`, `25/24`, `25/23.976`, plus a least-squares refinement over inliers. Choose the candidate with greatest coverage then lowest P90. Classify with the exact spec thresholds. `Apply` transforms every start/end, clamps starts to zero, preserves positive duration, sorts by start, and assigns indexes starting at 1.

- [ ] **Step 7: Run Task 1 tests and the full Core suite**

Run the focused filter, then `.run/dotnet/dotnet.exe test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj`. Record any pre-existing platform-only failure separately.

- [ ] **Step 8: Commit Task 1**

Commit: `feat: analyze and synchronize subtitle timing`

---

### Task 2: Reference extraction and acquisition policy

**Files:**
- Create: `src/NapisyPL.Core/Subtitles/ISubtitleCueReader.cs`
- Create: `src/NapisyPL.Core/Subtitles/SubtitleCueReader.cs`
- Create: `src/NapisyPL.Core/Subtitles/ISubtitleFallbackInteraction.cs`
- Modify: `src/NapisyPL.Core/Subtitles/SubtitleAcquisitionPipeline.cs`
- Modify: `tests/NapisyPL.Core.Tests/SubtitleAcquisitionPipelineTests.cs`

**Interfaces:**
- Consumes: Task 1 `SubtitleSynchronizationService` and `SubtitleTimingAnalysis`.
- Produces: `ISubtitleCueReader.ReadEmbeddedAsync(...)`, `ISubtitleCueReader.ReadFileAsync(...)`, `SubtitleFallbackRequest`, `SubtitleFallbackChoice`, `ISubtitleFallbackInteraction.ChooseAsync(...)`, `SubtitleAcquisitionPipeline.TranslateInteractiveAsync(...)`, and the reordered acquisition behavior. The existing `ITranslationPipeline.TranslateAsync` stays unchanged and noninteractive.

- [ ] **Step 1: Write failing acquisition tests for reference-first comparison**

Add fakes for the cue reader and fallback interaction. Assert that a safe downloaded Polish shift is transformed before save, a 100 ms aligned candidate keeps its original times, and an unsafe candidate is not written.

- [ ] **Step 2: Run acquisition tests and verify RED**

Run: `.run/dotnet/dotnet.exe test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj --filter FullyQualifiedName~SubtitleAcquisitionPipelineTests`

Expected: compilation fails for the new constructor dependencies and selector contracts.

- [ ] **Step 3: Implement the reference reader and Polish comparison path**

`SubtitleCueReader` uses `SubtitleExtractionService.ExtractToTemporarySrtAsync`, parses UTF-8 with `SrtParser`, validates cues, and deletes the temporary file in `finally`. It also reads a manually selected SRT without modifying it. Update `SubtitleAcquisitionPipeline` to read a selected text track once, analyze downloaded Polish cues, keep aligned timing, apply safe timing, and withhold unsafe output.

- [ ] **Step 4: Write failing tests for final fallback order and explicit alternative choices**

Assert the order: automatic Polish, alternative Polish, embedded English translation, downloaded English translation. Assert `ApplyRecommendedTransform` applies only the analyzer transform, `UseWithoutChanges` preserves the candidate, and `Cancel` proceeds to English. Verify the selector is never called when absent/disabled and translator initialization stays lazy for accepted Polish.

- [ ] **Step 5: Run acquisition tests and verify RED for fallback behavior**

Confirm the embedded-English-before-QNapi-English assertion fails under the old order.

- [ ] **Step 6: Implement the policy and alternative contract**

Implement `TranslateInteractiveAsync` on the concrete pipeline and a shared private core method; keep the interface method noninteractive. Exhaust automatic and selected Polish before translating. Prefer already extracted embedded English cues via `TranslateVideoSubtitlesAsync`; otherwise call the English QNApi pass. Preserve handled exception reporting, cancellation, atomic writes, and existing-output behavior. Treat a selected track as an automatic English MT source only when its language tag is `eng` or `en`; an untagged track may still be a timing reference but requires the explicit interaction choice before translation.

- [ ] **Step 7: Run focused and full Core tests**

Run acquisition and embedded-reader filters, then the complete Core test project.

- [ ] **Step 8: Commit Task 2**

Commit: `feat: validate Polish subtitles before translation`

---

### Task 3: Interactive QNapi and local SRT selection in Avalonia

**Files:**
- Modify: `src/NapisyPL.Core/Subtitles/ISubtitleDownloader.cs`
- Modify: `src/NapisyPL.Core/Subtitles/QnapiSubtitleDownloader.cs`
- Create: `src/NapisyPL/SubtitleAlternativeSelector.cs`
- Modify: `src/NapisyPL/MainWindow.axaml.cs`
- Modify: `src/NapisyPL/MainWindow.SubtitleSearch.cs`
- Modify: `src/NapisyPL/MainWindow.axaml`
- Modify: `tests/NapisyPL.Core.Tests/QnapiSubtitleDownloaderTests.cs`
- Modify: `tests/NapisyPL.Core.Tests/SubtitleSearchUiTests.cs`

**Interfaces:**
- Consumes: Task 2 `ISubtitleFallbackInteraction` and timing summary records.
- Produces: `IInteractiveSubtitleDownloader.DownloadInteractiveAsync(...)` and the Avalonia selector that can run QNapi or parse a user-picked SRT.

- [ ] **Step 1: Write failing QNapi tests for interactive invocation**

Assert interactive mode omits `-q`, retains isolated `-l/-lb`, parses the generated temporary sidecar, tolerates a longer timeout, and deletes its sidecar on success, cancellation, malformed output, and no selection.

- [ ] **Step 2: Run QNapi tests and verify RED**

Run the QNapi test filter and confirm the missing interactive interface causes failure.

- [ ] **Step 3: Implement interactive QNapi as a shared adapter path**

Extract common invocation logic without changing automatic arguments. Interactive mode uses shell-independent process launching, shows the QNapi window, waits for completion, parses only its owned random-extension sidecar, and cleans it in `finally`.

- [ ] **Step 4: Write failing static UI composition tests**

Assert the main window constructs `SubtitleCueReader`, `SubtitleSynchronizationService`, and the Avalonia fallback interaction; the interaction displays provider/coverage/offset/scale/P90; and the folder path calls only the noninteractive `ITranslationPipeline` entry point.

- [ ] **Step 5: Run UI composition tests and verify RED**

Run: `.run/dotnet/dotnet.exe test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj --filter "FullyQualifiedName~SubtitleSearchUiTests|FullyQualifiedName~QnapiSubtitleDownloaderTests"`

- [ ] **Step 6: Implement the Avalonia selector and wiring**

Show a compact modal only through `TranslateInteractiveAsync` in single-file processing. Offer interactive QNapi, local `.srt` selection, use without timing changes for review-grade candidates, or continue to English. Parse local SRT via `SubtitleCueReader` without modifying it. Folder processing continues to call the noninteractive interface method and has no mutable interaction flag.

- [ ] **Step 7: Update user-facing copy and QNapi documentation**

Explain that Polish candidates are compared with embedded English timing, that safe corrections are automatic, and that translation is the last fallback. Document the interactive QNapi window and manual SRT option in `docs/QNAPI.md`.

- [ ] **Step 8: Run the full build and test suite**

Run `.run/dotnet/dotnet.exe test NapisyPL.sln` and `.run/dotnet/dotnet.exe build NapisyPL.sln -c Release --no-restore`. Confirm no new warnings or failures.

- [ ] **Step 9: Commit Task 3**

Commit: `feat: offer alternative subtitle selection`

---

### Task 4: Scenario regression and release verification

**Files:**
- Create: `tests/NapisyPL.Core.Tests/SubtitleSynchronizationScenarioTests.cs`
- Modify: `README.md`
- Modify: `docs/QNAPI.md`

**Interfaces:**
- Consumes: the completed acquisition, synchronization, and UI flow.
- Produces: regression evidence for the supplied 802/768 cue shape without depending on the user's external `Z:` drive.

- [ ] **Step 1: Write a compact fixture representing the supplied sample**

Generate reference and candidate cue timelines with 802 versus 768 entries, merged cues, a 100 ms shift, and trailing credits. Assert scale `1.0`, `Aligned`, P90 below 350 ms, no applied rewrite, and machine-translation fallback only after the candidate is rejected.

- [ ] **Step 2: Run the scenario test and verify RED where integration is incomplete**

Run the scenario test filter. Any failure must identify a missing integration behavior rather than unavailable external media.

- [ ] **Step 3: Make the smallest integration correction needed for GREEN**

Adjust only the responsible Task 1–3 component. Do not add special cases for the episode title or hard-code the observed 100 ms offset.

- [ ] **Step 4: Run all tests and the Release build**

Run `.run/dotnet/dotnet.exe test NapisyPL.sln` and `.run/dotnet/dotnet.exe build NapisyPL.sln -c Release --no-restore`.

- [ ] **Step 5: Verify repository and launcher state**

Run `git status --short`, inspect the branch diff, and verify `Run-SubFlow.bat` accepts `codex/qnapi-subtitle-download` without changing branches.

- [ ] **Step 6: Commit Task 4**

Commit: `test: cover subtitle synchronization scenario`
