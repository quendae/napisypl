# SubFlow Offline MT Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reproducible offline EN→PL benchmark backends for Argos, Firefox/Bergamot, OPUS/Marian and NLLB-200 distilled 600M, producing directly comparable SRT outputs and timing/model metadata without changing the existing default Argos behavior.

**Architecture:** Keep `ITranslationProvider` as the application-facing abstraction, extract Argos text segmentation into one shared deterministic MT preprocessor, and place every heavyweight offline backend behind a persistent helper/runtime client. Bergamot and OPUS use the same pinned native `bergamot-translator v0.4.5` helper on Windows x64; NLLB uses an isolated pinned Python/Transformers helper and is marked benchmark-only. A benchmark coordinator feeds the same prepared parts to every selected backend, writes one SRT per backend plus JSON/TXT reports, and isolates failures per backend.

**Tech Stack:** .NET 10 / C#, Avalonia, JSON Lines helper protocol, Python 3.12 for helper tooling, `browsermt/bergamot-translator v0.4.5` native Windows x64 build with MSVC/CMake, Mozilla Firefox Translations EN→PL model registry, Helsinki-NLP Tatoeba `eng-pol/opus-2021-02-19.zip`, Hugging Face `facebook/nllb-200-distilled-600M`, Transformers 4.x, GitHub Actions Windows runners.

**Spec:** `docs/superpowers/specs/2026-09-12-subflow-offline-mt-benchmark-design.md`

## Global Constraints

- Work on branch `feature/local-translator-batch-v2`; PR #2 remains draft and MUST NOT be merged without explicit user request.
- `Local Argos (offline)` remains the default local backend and must preserve existing behavior.
- CPU is the common benchmark device for the first implementation; no CUDA/ROCm dependency is required.
- New heavyweight models are installed on demand and are not bundled into the main SubFlow ZIP.
- All four MT backends receive byte-for-byte identical prepared translation parts for a given input.
- Subtitle text must never be written to diagnostic logs.
- NLLB-600M is benchmark-only because its model license is CC-BY-NC-4.0 and its model card does not position it for production deployment.
- Keep the Better Call Saul regression sentinels: cue 141 must not be falsely feminized, cue 253 must retain third-person plural semantics and never regress to `Zapomniałaś/Zapomniałeś`, cue 437 must not be falsely feminized; cues 30 and 137 must retain both dialogue turns/content.
- Existing 3.2.5 regression tests remain mandatory.
- No unrelated refactor of Enhanced, diarization, addressee resolution or targeted Qwen review.

---

### Task 1: Extract the shared deterministic machine-translation preprocessor

**Files:**
- Create: `src/NapisyPL.Core/Translation/MachineTranslationTextPreprocessor.cs`
- Modify: `src/NapisyPL.Core/Translation/Providers/ArgosOfflineProvider.cs`
- Modify: `tests/NapisyPL.Core.Tests/ArgosOfflineProviderTests.cs`
- Modify: `tests/NapisyPL.Core.Tests/SubFlow325RegressionTests.cs`
- Create: `tests/NapisyPL.Core.Tests/MachineTranslationTextPreprocessorTests.cs`

**Interfaces:**
- Produces: `MachineTranslationPreparedBatch MachineTranslationTextPreprocessor.Prepare(IReadOnlyList<TranslationSegment> segments)`.
- Produces: `IReadOnlyDictionary<int,string> MachineTranslationTextPreprocessor.Reassemble(MachineTranslationPreparedBatch batch, IReadOnlyList<string> translatedParts)`.
- `MachineTranslationPreparedBatch` exposes original segment ids and a flattened `IReadOnlyList<string> Parts` in deterministic order.
- Argos consumes this component instead of owning `BuildLogicalLines`, dialogue-turn detection, abbreviation handling and sentence splitting.

- [ ] **Step 1: Write failing preprocessor parity tests**

Add tests that assert the exact flattened requests for:

```csharp
new TranslationSegment(253, "Do you think they're ever gonna\nforget today? Never.")
```

must produce:

```csharp
[
    "Do you think they're ever gonna forget today?",
    "Never."
]
```

and:

```csharp
new TranslationSegment(30, "<i>- Yeah, sí, problema.</i>\n<i>- And now dos problemas.</i>")
```

must produce exactly two dialogue parts, not one joined sentence.

Also assert abbreviation handling remains unchanged for `Mr.`, `Dr.`, `e.g.`, and that `Reassemble` throws `InvalidDataException` on translated-part count mismatch.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj --filter "MachineTranslationTextPreprocessorTests|SubFlow325RegressionTests|ArgosOfflineProviderTests"
```

Expected: new preprocessor tests fail because the type/API does not exist yet.

- [ ] **Step 3: Implement the minimal preprocessor extraction**

Move the current Argos logic unchanged into `MachineTranslationTextPreprocessor`:

```csharp
public sealed record MachineTranslationPreparedBatch(
    IReadOnlyList<TranslationSegment> OriginalSegments,
    IReadOnlyList<int> PartCounts,
    IReadOnlyList<string> Parts);

public sealed class MachineTranslationTextPreprocessor
{
    public const int SchemaVersion = 1;
    public MachineTranslationPreparedBatch Prepare(IReadOnlyList<TranslationSegment> segments);
    public IReadOnlyDictionary<int, string> Reassemble(
        MachineTranslationPreparedBatch batch,
        IReadOnlyList<string> translatedParts);
}
```

Preserve the current rules exactly: skip leading `<...>` / `{...}` tags before dialogue-turn detection, keep soft wraps joined, split on `. ! ? …` only after logical line reconstruction, and preserve the existing abbreviation set.

- [ ] **Step 4: Switch Argos to the shared preprocessor**

`ArgosOfflineProvider.TranslateAsync` becomes conceptually:

```csharp
var batch = _preprocessor.Prepare(segments);
var translated = batch.Parts.Count == 0
    ? []
    : await client.TranslateAsync(batch.Parts, cancellationToken);
return _preprocessor.Reassemble(batch, translated);
```

Keep the existing public constructor by adding an overload/default preprocessor so callers/tests do not break unnecessarily.

- [ ] **Step 5: Run focused tests and the full .NET suite**

Expected: all current Argos and 3.2.5 regressions stay green, with no changed semantic output.

```powershell
dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj
```

- [ ] **Step 6: Commit**

```bash
git add src/NapisyPL.Core/Translation tests/NapisyPL.Core.Tests
git commit -m "refactor: share offline MT preprocessing"
```

---

### Task 2: Add neutral offline-MT metadata, manifests and atomic asset installation

**Files:**
- Create: `src/NapisyPL.Core/OfflineMt/IOfflineMachineTranslatorClient.cs`
- Create: `src/NapisyPL.Core/OfflineMt/OfflineMachineTranslatorInfo.cs`
- Create: `src/NapisyPL.Core/OfflineMt/OfflineMtManifest.cs`
- Create: `src/NapisyPL.Core/OfflineMt/OfflineMtAssetManager.cs`
- Create: `src/NapisyPL.Core/OfflineMt/OfflineMtPaths.cs`
- Create: `tests/NapisyPL.Core.Tests/OfflineMtAssetManagerTests.cs`
- Create: `tests/NapisyPL.Core.Tests/OfflineMtManifestTests.cs`

**Interfaces:**

```csharp
public interface IOfflineMachineTranslatorClient
{
    string BackendId { get; }
    string DisplayName { get; }
    Task EnsureReadyAsync(IProgress<string>? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> TranslateAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default);
    OfflineMachineTranslatorInfo GetInfo();
}

public sealed record OfflineMachineTranslatorInfo(
    string BackendId,
    string DisplayName,
    string ModelId,
    string ModelVersion,
    string ModelSource,
    string ModelSha256,
    long InstalledSizeBytes,
    string RuntimeName,
    string RuntimeVersion,
    string Device,
    string LicenseId,
    bool BenchmarkOnly);
```

`OfflineMtPaths.CreateDefault()` roots new assets at `%LocalAppData%\SubFlow\offline-mt`.

- [ ] **Step 1: Write RED tests for manifest validation and atomic install**

Tests must cover:
- missing manifest => not installed,
- manifest whose required file is missing => not installed,
- wrong SHA-256 => not installed,
- interrupted `.tmp-*` directory => ignored/cleanable,
- valid manifest + all hashes => installed,
- NLLB metadata can carry `BenchmarkOnly=true` and `LicenseId="CC-BY-NC-4.0"`.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj --filter "OfflineMtAssetManagerTests|OfflineMtManifestTests"
```

- [ ] **Step 3: Implement immutable manifest types and SHA-256 verification**

Use `System.Security.Cryptography.SHA256` and UTF-8 JSON. `OfflineMtAssetManager` must only promote a temporary install directory into the final backend directory after every declared file exists and matches its expected hash.

- [ ] **Step 4: Implement backend directories**

Exact default layout:

```text
%LocalAppData%\SubFlow\offline-mt\bergamot\
%LocalAppData%\SubFlow\offline-mt\opus-marian\
%LocalAppData%\SubFlow\offline-mt\nllb-600m\
```

Do not move or rename the existing Argos cache/layout in this task.

- [ ] **Step 5: Run focused and full tests**

- [ ] **Step 6: Commit**

```bash
git add src/NapisyPL.Core/OfflineMt tests/NapisyPL.Core.Tests
git commit -m "feat: add offline MT asset manifests"
```

---

### Task 3: Prove and package a Windows x64 Bergamot runtime helper before wiring providers

**Why this task is first among new engines:** PyPI `bergamot 0.4.5` publishes Linux/macOS wheels but no Windows wheel. The upstream `setup.py` contains MSVC/x64 CMake support, and release `v0.4.5` is pinned. We therefore build our Windows runtime in CI from the upstream tag instead of relying on a nonexistent Windows binary wheel.

**Files:**
- Create: `tools/offline-mt/bergamot-helper/CMakeLists.txt`
- Create: `tools/offline-mt/bergamot-helper/main.cpp`
- Create: `tools/offline-mt/bergamot-helper/README.md`
- Create: `tools/offline-mt/test_bergamot_protocol.py`
- Create: `.github/workflows/offline-mt-runtime.yml`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces Windows executable `SubFlow.BergamotHelper.exe`.
- Helper speaks existing JSONL shapes: `load`, `translate`, `shutdown` in; `ready`, `progress`, `segment`, `complete`, `error` out.
- `load.modelPath` points to a backend-specific directory containing a generated Bergamot config plus model/vocab/shortlist files.

- [ ] **Step 1: Add a protocol test that initially fails because the helper artifact is absent**

`tools/offline-mt/test_bergamot_protocol.py` launches `SubFlow.BergamotHelper.exe`, sends malformed JSON and a shutdown command, and verifies a structured `error`/clean exit without ever logging input text.

- [ ] **Step 2: Add the pinned upstream build workflow**

The Windows workflow checks out `browsermt/bergamot-translator` at tag `v0.4.5`, initializes submodules, configures with MSVC/CMake for x64 Release, builds the static/native translator library, then builds our small JSONL wrapper executable against it.

Pin exactly:

```text
repository: https://github.com/browsermt/bergamot-translator.git
tag: v0.4.5
runtime version: 0.4.5
```

Do not `pip install bergamot` on Windows because no Windows wheel exists on PyPI for 0.4.5.

- [ ] **Step 3: Implement the JSONL wrapper with no subtitle logging**

`main.cpp` must:
- read one UTF-8 JSON object per stdin line,
- hold the loaded Bergamot service/model in memory across multiple `translate` commands,
- preserve segment ids/order,
- emit one `segment` event per result,
- return structured codes such as `invalid_command`, `model_load_failed`, `translation_failed`,
- never print source or translated text to stderr.

Use a small vendored/header-only JSON parser only if already permitted by the repo; otherwise parse the minimal fixed protocol with an explicitly added MIT-licensed single-header dependency recorded in `README.md`.

- [ ] **Step 4: Build the helper on Windows CI and run protocol smoke tests**

Expected: protocol test passes before any large model download.

- [ ] **Step 5: Add a tiny model-load integration lane or manual workflow input**

The runtime workflow must support a manually triggered integration step that downloads one real Bergamot-compatible model and verifies `Hello.` returns a non-empty translated segment. Keep the large/model-network integration separate from ordinary unit CI if runtime exceeds the normal budget.

- [ ] **Step 6: Commit**

```bash
git add tools/offline-mt .github/workflows
git commit -m "feat: build Windows Bergamot helper"
```

---

### Task 4: Add Firefox/Bergamot EN→PL and OPUS/Marian EN→PL asset resolvers and clients

**Files:**
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/BergamotRuntimeOptions.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/BergamotRuntimeManager.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/BergamotModelDescriptor.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/FirefoxBergamotAssetManager.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/OpusMarianAssetManager.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/FirefoxBergamotTranslatorClient.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Bergamot/OpusMarianTranslatorClient.cs`
- Create: `tests/NapisyPL.Core.Tests/FirefoxBergamotAssetManagerTests.cs`
- Create: `tests/NapisyPL.Core.Tests/OpusMarianAssetManagerTests.cs`
- Create: `tests/NapisyPL.Core.Tests/BergamotRuntimeManagerTests.cs`

**Interfaces:**
- Both clients implement `IOfflineMachineTranslatorClient`.
- Both clients share one process/runtime implementation but have separate asset directories and metadata.
- Firefox backend id: `bergamot-firefox-en-pl`.
- OPUS backend id: `opus-marian-eng-pol-2021-02-19`.

- [ ] **Step 1: Write RED tests for model descriptor resolution**

Firefox tests use an injected fake registry JSON and assert selection of English→Polish (`enpl`) with the requested architecture preference `base` first, then `base-memory`, then `tiny` only if no larger variant is present. Record the exact URLs/hashes returned by the registry into the manifest rather than hardcoding stale GCS object URLs.

OPUS tests pin:

```text
https://object.pouta.csc.fi/Tatoeba-MT-models/eng-pol/opus-2021-02-19.zip
source = eng
target = pol
release = 2021-02-19
preprocessing = normalization + SentencePiece spm32k/spm32k
```

The downloaded ZIP hash is computed after download and written to the manifest; extracted required files get their own SHA-256 entries.

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Implement Firefox registry resolver**

Fetch Mozilla's current public model registry JSON during installation only. Resolve one EN→PL model, download its declared files, verify declared hashes where supplied, generate the Bergamot config file in the backend model directory, then atomically promote the install.

- [ ] **Step 4: Implement OPUS/Tatoeba resolver**

Download the pinned `eng-pol/opus-2021-02-19.zip`, reject path traversal entries, extract into a temp directory, verify required `model.npz`/model file, source/target SentencePiece vocab files and config, then generate the Bergamot-compatible config required by our helper. If an explicit one-time conversion/config patch is required, perform it during installation and include every generated file hash in the manifest.

- [ ] **Step 5: Implement the shared persistent runtime manager**

Follow the proven Argos process pattern: one child process, UTF-8 no BOM, redirected stdin/stdout/stderr, semaphore gate, two-minute load timeout, structured JSONL events, clean shutdown then process-tree kill fallback. Do not duplicate the full Argos class; extract only a small reusable process-channel utility if doing so reduces duplication without changing Argos behavior.

- [ ] **Step 6: Run tests and manual real-model smoke for both engines**

Use the same short EN inputs for both clients and assert one output per input. Do not compare exact Polish in unit tests; integration smoke only requires non-empty output and stable segment count.

- [ ] **Step 7: Commit**

```bash
git add src/NapisyPL.Core/OfflineMt/Bergamot tests/NapisyPL.Core.Tests
git commit -m "feat: add Bergamot and OPUS offline backends"
```

---

### Task 5: Add the NLLB-200 distilled 600M benchmark-only helper and client

**Files:**
- Create: `tools/offline-mt/nllb_helper.py`
- Create: `tools/offline-mt/requirements-nllb.txt`
- Create: `tools/offline-mt/test_nllb_protocol.py`
- Create: `src/NapisyPL.Core/OfflineMt/Nllb/NllbRuntimeOptions.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Nllb/NllbAssetManager.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Nllb/NllbRuntimeManager.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Nllb/NllbTranslatorClient.cs`
- Create: `tests/NapisyPL.Core.Tests/NllbAssetManagerTests.cs`
- Create: `tests/NapisyPL.Core.Tests/NllbTranslatorClientTests.cs`
- Modify: `.github/workflows/offline-mt-runtime.yml`

**Interfaces:**
- Backend id: `nllb-200-distilled-600m-eng-pol`.
- Model id: `facebook/nllb-200-distilled-600M`.
- Source language token: `eng_Latn`.
- Target language token: `pol_Latn`.
- `GetInfo()` must return `BenchmarkOnly=true`, `LicenseId="CC-BY-NC-4.0"`.

- [ ] **Step 1: Write RED protocol and metadata tests**

Assert malformed commands return structured errors, translation preserves ids/count, and metadata cannot omit the benchmark-only/license flags.

- [ ] **Step 2: Pin the Python environment**

`requirements-nllb.txt` must pin a Transformers 4.x version known to load this checkpoint, plus matching `torch`, `sentencepiece` and `safetensors` versions. Do not depend on Transformers 5.x high-level translation pipeline.

- [ ] **Step 3: Implement `nllb_helper.py`**

Load with `AutoTokenizer.from_pretrained` and `AutoModelForSeq2SeqLM.from_pretrained`, set `src_lang="eng_Latn"`, generate with forced BOS for `pol_Latn`, `model.eval()`, `torch.inference_mode()`, CPU device, and batch short subtitle parts in bounded groups. The helper uses the same JSONL event shapes and never logs sentence contents.

- [ ] **Step 4: Implement asset/runtime manager**

Install into `%LocalAppData%\SubFlow\offline-mt\nllb-600m`; keep Hugging Face files local to that backend directory so the benchmark manifest can hash/size them deterministically. Runtime startup must use offline/local-files-only mode after installation.

- [ ] **Step 5: Add separate manual/large integration workflow step**

The ordinary CI tests helper protocol without downloading ~2.5 GB. A manual workflow input downloads the real model and translates `Hello, how are you?` to a non-empty result.

- [ ] **Step 6: Commit**

```bash
git add tools/offline-mt src/NapisyPL.Core/OfflineMt/Nllb tests/NapisyPL.Core.Tests .github/workflows/offline-mt-runtime.yml
git commit -m "feat: add NLLB offline benchmark backend"
```

---

### Task 6: Add generic offline MT providers, factory/UI selection and cache identity isolation

**Files:**
- Create: `src/NapisyPL.Core/Translation/Providers/OfflineMachineTranslationProvider.cs`
- Create: `src/NapisyPL.Core/OfflineMt/OfflineMtRuntimeRegistry.cs`
- Modify: `src/NapisyPL.Core/Translation/ProviderFactory.cs`
- Modify: `src/NapisyPL.Core/Translation/ProviderUiProfile.cs`
- Modify: `src/NapisyPL/MainWindow.Argos.cs`
- Modify: `src/NapisyPL/Settings/AppSettings.cs` only if provider persistence currently validates a fixed list
- Modify: `src/NapisyPL.Core/Services/EnhancedTranslationCache.cs`
- Create: `tests/NapisyPL.Core.Tests/OfflineMachineTranslationProviderTests.cs`
- Modify: `tests/NapisyPL.Core.Tests/ProviderFactoryArgosTests.cs`
- Modify: `tests/NapisyPL.Core.Tests/ProviderUiProfileTests.cs`
- Modify: `tests/NapisyPL.Core.Tests/EnhancedTranslationCacheTests.cs`

**Interfaces:**
- Provider display names exactly:
  - `Local Firefox/Bergamot (offline, benchmark)`
  - `Local OPUS/Marian (offline, benchmark)`
  - `Local NLLB-600M (offline, benchmark)`
- `OfflineMachineTranslationProvider` uses `MachineTranslationTextPreprocessor` and an `IOfflineMachineTranslatorClient`.
- Provider cache identity format:

```text
offline-mt|{backendId}|{modelVersionOrHash}|preprocess-v{MachineTranslationTextPreprocessor.SchemaVersion}
```

- [ ] **Step 1: Write RED factory/UI/provider tests**

Assert all three providers hide API/model/Base URL inputs, NLLB hint contains benchmark-only/license notice, and provider output count mismatch is rejected.

- [ ] **Step 2: Write RED cache isolation tests**

Save the same source with `argos|...`, `bergamot-firefox|...`, `opus-marian|...` and `nllb|...`; each identity must read only its own cache entry. Also assert changing preprocessor schema/model hash creates a miss.

- [ ] **Step 3: Implement provider + runtime registry**

Keep lazy initialization: selecting a backend may show install status, but model/runtime download starts only after explicit install/translation action according to existing UI flow.

- [ ] **Step 4: Wire `ProviderFactory` and UI profiles**

Do not change `Local Argos (offline)` behavior or hint text except where a shared helper method reduces duplication. Add provider-specific hints and install-size/license status text for experimental backends.

- [ ] **Step 5: Update cache identity handling without invalidating unrelated entries unnecessarily**

Use the provider identity already accepted by `EnhancedTranslationCache`; do not add model logic inside the cache itself if the caller can supply a complete identity. Bump `CacheVersion` only if the serialized cache format changes, not merely because new identities exist.

- [ ] **Step 6: Run full .NET tests**

- [ ] **Step 7: Commit**

```bash
git add src/NapisyPL.Core src/NapisyPL tests/NapisyPL.Core.Tests
git commit -m "feat: expose offline MT benchmark providers"
```

---

### Task 7: Build the benchmark coordinator and reports

**Files:**
- Create: `src/NapisyPL.Core/OfflineMt/Benchmark/OfflineMtBenchmarkRequest.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Benchmark/OfflineMtBenchmarkResult.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Benchmark/OfflineMtBenchmarkService.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Benchmark/OfflineMtBenchmarkReportWriter.cs`
- Create: `src/NapisyPL.Core/OfflineMt/Benchmark/BenchmarkSrtWriter.cs`
- Create: `tools/offline-mt/README.md`
- Create: `tests/NapisyPL.Core.Tests/OfflineMtBenchmarkServiceTests.cs`
- Create: `tests/NapisyPL.Core.Tests/OfflineMtBenchmarkReportWriterTests.cs`

**Interfaces:**

```csharp
public sealed record OfflineMtBenchmarkRequest(
    string InputPath,
    string OutputDirectory,
    IReadOnlyList<string> BackendIds);

public sealed record OfflineMtBackendBenchmarkResult(
    OfflineMachineTranslatorInfo Info,
    TimeSpan ColdLoadTime,
    TimeSpan TranslationTime,
    TimeSpan TotalTime,
    int SegmentCount,
    int CharacterCount,
    double SegmentsPerSecond,
    double CharactersPerSecond,
    long? PeakWorkingSetBytes,
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string? OutputSrtPath);
```

- [ ] **Step 1: Write RED fairness tests using fake clients**

Use four fake clients that capture received strings. Assert every backend receives exactly the same flattened `Parts` sequence from one preprocessing pass and that reassembly produces one output cue per input cue.

- [ ] **Step 2: Write RED partial-failure tests**

Backend B throws; A/C/D still run and persist outputs. `report.json` contains B as failed and the global run is considered usable if at least one backend succeeds.

- [ ] **Step 3: Implement timings and process working-set sampling**

Measure cold load and translation separately with `Stopwatch`. Peak working set is nullable; record it only when the helper process/runtime exposes a reliable process id without adding a heavy dependency.

- [ ] **Step 4: Implement deterministic output layout**

One run directory:

```text
benchmark-YYYYMMDD-HHMMSS/
  argos.srt
  bergamot.srt
  opus-marian.srt
  nllb-600m.srt
  report.json
  report.txt
```

`report.json` includes input SHA-256 and `MachineTranslationTextPreprocessor.SchemaVersion`.

- [ ] **Step 5: Implement human-readable TXT report**

Columns/sections must include backend, model/runtime, installed size, load time, translation time, total time, segments/s, chars/s, success/failure and output filename. Do not include subtitle contents.

- [ ] **Step 6: Run tests**

- [ ] **Step 7: Commit**

```bash
git add src/NapisyPL.Core/OfflineMt/Benchmark tools/offline-mt tests/NapisyPL.Core.Tests
git commit -m "feat: add offline MT benchmark harness"
```

---

### Task 8: Add a developer entry point and benchmark documentation without bloating the main GUI

**Files:**
- Create: `tools/offline-mt/run-benchmark.ps1`
- Modify: `docs/LOCAL_TRANSLATION_TESTING.md`
- Modify: `README.md`
- Modify: `.github/workflows/offline-mt-runtime.yml`

**Interfaces:**
- PowerShell entry point accepts:

```powershell
./tools/offline-mt/run-benchmark.ps1 `
  -Input "C:\path\episode.srt" `
  -Backends argos,bergamot,opus-marian,nllb-600m `
  -Output "C:\path\benchmarks"
```

- It must print final artifact paths and a one-line timing table, not subtitle contents.

- [ ] **Step 1: Add script argument validation tests or dry-run mode**

Provide `-DryRun` that validates backend ids/paths and prints the planned benchmark without model downloads.

- [ ] **Step 2: Implement the developer launcher**

Use the existing published/CLI-capable application entry point if available; otherwise add a narrowly scoped benchmark command-line switch in `Program.cs` that bypasses Avalonia window creation and invokes `OfflineMtBenchmarkService`.

- [ ] **Step 3: Document install sizes/licensing and benchmark semantics**

Clearly state that Argos remains default and NLLB is benchmark-only; document first-run downloads and where assets are stored.

- [ ] **Step 4: Commit**

```bash
git add tools/offline-mt docs/LOCAL_TRANSLATION_TESTING.md README.md src/NapisyPL/Program.cs .github/workflows/offline-mt-runtime.yml
git commit -m "docs: add offline MT benchmark workflow"
```

---

### Task 9: Run the full regression/CI gate and produce the first Better Call Saul benchmark artifact

**Files:**
- Modify only if failures reveal defects in the implementation; do not change sentinel expectations to make failures disappear.
- Benchmark output is an artifact, not committed source.

**Interfaces:**
- CI must keep existing .NET, Argos helper and local translator checks green.
- New offline-MT workflow must at minimum prove the Bergamot Windows helper build and protocol tests on every relevant source change.
- Large real-model integrations may remain manual/workflow-dispatch if download/runtime cost is excessive.

- [ ] **Step 1: Run all .NET tests**

```powershell
dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj
```

Expected: zero failures; test count greater than the pre-feature 170 baseline.

- [ ] **Step 2: Run Python/helper protocol tests**

```powershell
python -m unittest discover -s tools/local-translator -p "test_*.py" -v
python -m unittest discover -s tools/offline-mt -p "test_*.py" -v
```

- [ ] **Step 3: Push each RED/GREEN milestone and inspect GitHub Actions**

Do not declare success from local reasoning alone. Confirm the relevant PR-triggered CI run(s), job steps and logs.

- [ ] **Step 4: Run real-model benchmark on the Better Call Saul S01E02 source**

Generate outputs for all installed/successful backends. Verify automatically that cue count remains 471 and inspect manually:
- cue 30,
- cue 137,
- cue 141,
- cue 247,
- cue 252,
- cue 253,
- cue 437.

- [ ] **Step 5: Produce the decision table**

For each backend report:
- installed size,
- cold load,
- translation time for 471 cues,
- throughput,
- obvious omissions/content loss,
- naturalness observations,
- gender/person errors,
- license suitability.

Do not pick a new default until this benchmark is reviewed with the user.

- [ ] **Step 6: Keep PR #2 draft**

No merge. Attach/link the successful Windows artifact and benchmark report for user testing.

- [ ] **Step 7: Final verification commit only if documentation/results metadata changed**

```bash
git status
git log -1 --oneline
```

Confirm the branch head and CI status before reporting completion.

---

## Plan self-review

- **Spec coverage:** shared preprocessing, four backends, asset manifests, on-demand install, identical benchmark input, cache isolation, logging/privacy, partial failures, JSON/TXT reports, Windows-first runtime, licensing and BCS sentinels all map to explicit tasks above.
- **Risk handling:** the largest uncertainty is native Bergamot on Windows; Task 3 isolates and proves that runtime before provider/UI work depends on it. The public PyPI release has no Windows wheel, so the plan intentionally builds pinned `v0.4.5` from source with MSVC rather than assuming a binary exists.
- **Type consistency:** all new providers consume `IOfflineMachineTranslatorClient`; all machine-translation providers share `MachineTranslationTextPreprocessor`; benchmark results consume `OfflineMachineTranslatorInfo` from the same clients.
- **Scope:** Enhanced/Qwen/diarization are deliberately untouched except for consuming the chosen baseline translation later; model-quality selection remains a post-benchmark decision.
