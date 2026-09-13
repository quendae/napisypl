# NapisyPL v2 Local Translator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an install-on-demand offline English→Polish translator using an isolated Argos Translate/CTranslate2 helper process while keeping the Avalonia application independent from Python at runtime.

**Architecture:** Build a Windows x64 helper as a versioned zipped onedir bundle using Python + Argos Translate + PyInstaller. NapisyPL downloads the helper package and the official Argos `en→pl` model on first use, verifies installation through a local manifest, starts one helper process per translation session, and communicates through JSON Lines over redirected stdin/stdout; the helper remains loaded for an entire folder queue.

**Tech Stack:** .NET 10, Avalonia 12, Python 3.x build environment, Argos Translate/CTranslate2, PyInstaller, GitHub Actions, xUnit.

**Spec:** `docs/superpowers/specs/2026-09-10-napisypl-local-batch-design.md`

## Global Constraints

- Local provider works offline after helper + model installation.
- CPU is the default and CUDA/GPU is not required.
- Model source is the official Argos package index; select by `from_code=en`, `to_code=pl` instead of pinning application logic to one package version.
- Helper runs without a console window and is terminated on cancel/app close.
- Model is loaded once for a translation session/folder, not once per batch.
- Protocol never logs subtitle text.
- Failed/partial downloads must not be treated as installed.
- Existing cloud providers remain unchanged from the user's point of view.

---

### Task 1: Define JSONL helper protocol in .NET

**Files:**
- Create: `src/NapisyPL.Core/LocalTranslation/LocalTranslatorProtocol.cs`
- Test: `tests/NapisyPL.Core.Tests/LocalTranslatorProtocolTests.cs`

**Interfaces:**
- Produces command records: `LoadCommand`, `TranslateCommand`, `ShutdownCommand`.
- Produces event records: `ReadyEvent`, `ProgressEvent`, `SegmentEvent`, `CompleteEvent`, `ErrorEvent`.
- Produces `SerializeCommand(...)` and `ParseEvent(string jsonLine)`.

- [ ] **Step 1: Write failing round-trip tests**

```csharp
var line = LocalTranslatorProtocol.SerializeCommand(
    new TranslateCommand("job-1", [new TranslationSegment(7, "Hello") ]));
Assert.Contains("\"type\":\"translate\"", line);

var evt = LocalTranslatorProtocol.ParseEvent(
    "{\"type\":\"segment\",\"jobId\":\"job-1\",\"id\":7,\"text\":\"Cześć\"}");
Assert.IsType<SegmentEvent>(evt);
```

- [ ] **Step 2: Run targeted test and verify RED**

Run: `dotnet test tests/NapisyPL.Core.Tests/NapisyPL.Core.Tests.csproj -c Release --filter LocalTranslatorProtocolTests`

- [ ] **Step 3: Implement strict discriminated parsing**

Use `System.Text.Json`; reject unknown `type`, missing IDs/job IDs, and malformed JSON with `InvalidDataException`.

- [ ] **Step 4: Run tests and commit**

```bash
git add src/NapisyPL.Core/LocalTranslation tests/NapisyPL.Core.Tests/LocalTranslatorProtocolTests.cs
git commit -m "feat: define local translator JSONL protocol"
```

---

### Task 2: Implement the Python Argos helper

**Files:**
- Create: `tools/local-translator/translator.py`
- Create: `tools/local-translator/requirements.txt`
- Create: `tools/local-translator/README.md`
- Create: `tools/local-translator/test_protocol.py`

**Interfaces:**
- stdin: one UTF-8 JSON object per line.
- stdout: protocol events only, one UTF-8 JSON object per line.
- stderr: runtime diagnostics only, never subtitle text.

- [ ] **Step 1: Write protocol tests before implementation**

Use Python `unittest`; test `shutdown`, malformed request handling, and translation dispatch through an injected fake translator function so unit tests do not require downloading a model.

- [ ] **Step 2: Run test and verify RED**

Run: `python -m unittest tools/local-translator/test_protocol.py -v`

- [ ] **Step 3: Implement the line loop**

Core shape:

```python
def emit(payload):
    print(json.dumps(payload, ensure_ascii=False), flush=True)

for line in sys.stdin:
    command = json.loads(line)
    if command["type"] == "load":
        translator = load_argos_translation(command["modelPath"])
        emit({"type": "ready", "model": "en-pl", "version": MODEL_VERSION})
    elif command["type"] == "translate":
        total = len(command["segments"])
        for completed, segment in enumerate(command["segments"], start=1):
            text = translator.translate(segment["text"])
            emit({"type": "segment", "jobId": command["jobId"], "id": segment["id"], "text": text})
            emit({"type": "progress", "jobId": command["jobId"], "completed": completed, "total": total})
        emit({"type": "complete", "jobId": command["jobId"]})
    elif command["type"] == "shutdown":
        break
```

Keep Argos loading behind a function so tests can inject a fake.

- [ ] **Step 4: Pin helper dependencies**

`requirements.txt` must pin exact versions verified by the helper build workflow. Do not use floating `latest` dependencies.

- [ ] **Step 5: Run Python unit tests and commit**

```bash
python -m unittest tools/local-translator/test_protocol.py -v
git add tools/local-translator
git commit -m "feat: add Argos local translator helper"
```

---

### Task 3: Package helper as Windows x64 onedir ZIP

**Files:**
- Create: `.github/workflows/local-translator.yml`
- Create: `tools/local-translator/local-translator.spec`

**Interfaces:**
- Produces CI artifact `NapisyPL.LocalTranslator-win-x64.zip`.
- ZIP root contains `NapisyPL.LocalTranslator.exe` plus bundled native/runtime files.

- [ ] **Step 1: Add PyInstaller spec**

Use onedir rather than onefile so CTranslate2 native DLLs and package data are explicit and startup overhead is lower.

- [ ] **Step 2: Add Windows workflow**

Workflow steps: checkout → setup Python x64 → install pinned requirements + PyInstaller → run helper unit tests → build with spec → invoke helper with a `shutdown` command as packaging smoke test → zip `dist/NapisyPL.LocalTranslator/` → upload artifact.

- [ ] **Step 3: Verify CI artifact manually**

Download artifact and inspect that executable plus required DLL/package data exist.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/local-translator.yml tools/local-translator/local-translator.spec
git commit -m "build: package local translator helper"
```

---

### Task 4: Add model index and installation manager

**Files:**
- Create: `src/NapisyPL.Core/LocalTranslation/ArgosPackageIndex.cs`
- Create: `src/NapisyPL.Core/LocalTranslation/LocalTranslatorManifest.cs`
- Create: `src/NapisyPL.Core/LocalTranslation/LocalTranslatorManager.cs`
- Test: `tests/NapisyPL.Core.Tests/ArgosPackageIndexTests.cs`
- Test: `tests/NapisyPL.Core.Tests/LocalTranslatorManagerTests.cs`

**Interfaces:**
- `ArgosPackageIndex.FindLatest(string json, string source, string target)` returns package metadata for `en→pl`.
- `LocalTranslatorManager.GetStateAsync()` returns `NotInstalled`, `HelperMissing`, `ModelMissing`, or `Ready`.
- `EnsureInstalledAsync(IProgress<LocalInstallProgress>, CancellationToken)` performs atomic installation.

- [ ] **Step 1: Write failing package-index tests**

Feed a fixture containing multiple versions and language pairs. Assert only `from_code=en`, `to_code=pl` is selected and the highest semantic version is returned.

- [ ] **Step 2: Write failing installation tests with fake HTTP handler**

Cover cached ready install, incomplete helper ZIP, interrupted model download, and temp-directory cleanup.

- [ ] **Step 3: Implement installation layout**

Use:

```text
%LocalAppData%/NapisyPL/local-translator/
  manifest.json
  helper/<version>/...
  models/en-pl/<version>/...
  temp/...
```

Download into a uniquely named temp directory, validate required files, write manifest last, then atomically move into the final versioned directory.

- [ ] **Step 4: Add SHA-256 verification for helper release asset**

The helper release must publish a checksum alongside the ZIP. `LocalTranslatorManager` downloads the checksum first and refuses a mismatched archive.

For the Argos model, persist upstream package metadata/version and validate that the downloaded archive can be opened before installing it.

- [ ] **Step 5: Run tests and commit**

```bash
dotnet test NapisyPL.sln -c Release
git add src/NapisyPL.Core/LocalTranslation tests/NapisyPL.Core.Tests
git commit -m "feat: install local translator and Argos model"
```

---

### Task 5: Build long-lived helper client

**Files:**
- Create: `src/NapisyPL.Core/LocalTranslation/LocalTranslatorClient.cs`
- Test: `tests/NapisyPL.Core.Tests/LocalTranslatorClientTests.cs`

**Interfaces:**
- `StartAsync(...)` starts helper and waits for `ready`.
- `TranslateAsync(IReadOnlyList<TranslationSegment>, IProgress<LocalTranslationProgress>?, CancellationToken)` returns translations.
- `DisposeAsync()` sends shutdown, then kills the process tree if graceful exit fails.

- [ ] **Step 1: Write failing client tests against a fake helper executable/script fixture**

Cover ready handshake, ordered segment collection, progress events, helper crash, malformed JSON, cancellation, and process cleanup.

- [ ] **Step 2: Run test and verify RED**

- [ ] **Step 3: Implement process management**

Use `ProcessStartInfo` with redirected stdin/stdout/stderr, UTF-8, `UseShellExecute=false`, `CreateNoWindow=true`. Serialize writes through a `SemaphoreSlim`. Read stdout continuously and route events by `jobId`.

- [ ] **Step 4: Run tests and commit**

```bash
dotnet test NapisyPL.sln -c Release
git add src/NapisyPL.Core/LocalTranslation tests/NapisyPL.Core.Tests/LocalTranslatorClientTests.cs
git commit -m "feat: manage local translator helper process"
```

---

### Task 6: Add `Lokalny (Argos)` provider

**Files:**
- Create: `src/NapisyPL.Core/Translation/Providers/LocalArgosProvider.cs`
- Modify: `src/NapisyPL.Core/Translation/ProviderFactory.cs`
- Test: `tests/NapisyPL.Core.Tests/LocalArgosProviderTests.cs`

**Interfaces:**
- Consumes a session-owned `LocalTranslatorClient` rather than constructing a process per batch.
- `BatchPolicy = new TranslationBatchPolicy(20, 6000)` initially; helper still emits per-segment progress internally.

- [ ] **Step 1: Write failing provider test**

Use a fake client; assert IDs are preserved and API key/model/Base URL are unnecessary.

- [ ] **Step 2: Implement provider and factory path**

Do not overload the existing cloud `ProviderFactory.Create(...)` with hidden process startup. Add a separate local creation path accepting an already-started client, e.g. `ProviderFactory.CreateLocal(LocalTranslatorClient client)`.

- [ ] **Step 3: Run tests and commit**

```bash
dotnet test NapisyPL.sln -c Release
git add src/NapisyPL.Core tests/NapisyPL.Core.Tests
git commit -m "feat: add offline Argos translation provider"
```

---

### Task 7: Keep one local model loaded for a whole folder job

**Files:**
- Modify: `src/NapisyPL/MainWindow.axaml.cs`
- Modify: `src/NapisyPL.Core/Services/FolderBatchService.cs`
- Test: `tests/NapisyPL.Core.Tests/FolderBatchServiceTests.cs`

**Interfaces:**
- UI owns one `LocalTranslatorClient` for the operation lifetime.
- The same `LocalArgosProvider` instance is supplied to every file in the folder.

- [ ] **Step 1: Add failing lifetime test**

Use a counting fake local session and assert a three-file folder starts/loads exactly once and executes three file translations.

- [ ] **Step 2: Implement operation-scoped local session**

When provider is `Lokalny (Argos)`: ensure installed → start helper → load model → create provider → run single file/folder → dispose session in `finally`.

- [ ] **Step 3: Run tests and commit**

```bash
dotnet test NapisyPL.sln -c Release
git add src tests
git commit -m "feat: reuse offline model across folder batches"
```

---

### Task 8: Local provider UI and installer progress

**Files:**
- Modify: `src/NapisyPL/MainWindow.axaml`
- Modify: `src/NapisyPL/MainWindow.axaml.cs`
- Modify: `src/NapisyPL/Settings/AppSettings.cs`

- [ ] **Step 1: Add provider option**

Add `Lokalny (Argos)` to the provider combo. When selected, hide/disable API key, model, and Base URL rows and show one status row: `Nie zainstalowano`, `Pobieranie…`, or `Offline · EN→PL gotowy`.

- [ ] **Step 2: Add install/reinstall action**

Show `Pobierz translator` when missing and `Przeinstaluj translator` when ready. Display helper/model download phase and byte progress when `Content-Length` is available.

- [ ] **Step 3: Preserve cancellation and errors**

Cancel must terminate install or helper operation cleanly. Installation errors show a friendly message and write metadata to the normal app log.

- [ ] **Step 4: Compile/publish and commit**

```bash
dotnet test NapisyPL.sln -c Release
dotnet publish src/NapisyPL/NapisyPL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
git add src/NapisyPL
git commit -m "feat: add offline translator UI"
```

---

### Task 9: Quality/performance benchmark harness

**Files:**
- Create: `benchmarks/subtitles-en.srt`
- Create: `tools/benchmark/README.md`
- Create: `tools/benchmark/BenchmarkRunner.cs`
- Create: `tools/benchmark/NapisyPL.Benchmark.csproj`

**Interfaces:**
- Takes provider + model/configuration and writes CSV/JSON metrics: provider, model, segments, characters, elapsed, segments/s, chars/s.
- Does not automatically grade translation quality.

- [ ] **Step 1: Create a representative English sample**

Include short dialogue, contractions, slang, multiline cues, names, punctuation, and formatting tags; write original test text, not copied film subtitles.

- [ ] **Step 2: Implement timing harness around existing pipeline/provider contracts**

Output original source ID, translated text, and timing to a local benchmark result file intended for manual comparison; this developer tool may contain the benchmark fixture text but production logs must not.

- [ ] **Step 3: Run Argos benchmark on Windows test machine**

Record cold-start time separately from warm translation throughput so model loading does not distort per-line speed.

- [ ] **Step 4: Compare manually with DeepL/Gemini using the same fixture**

Create a small markdown scorecard: naturalness, slang, line brevity, names/tags, obvious mistranslations. Do not claim automated quality parity from throughput alone.

- [ ] **Step 5: Commit**

```bash
git add benchmarks tools/benchmark
git commit -m "test: add subtitle translation benchmark harness"
```

---

### Task 10: Release workflow and end-to-end verification

**Files:**
- Create/Modify: `.github/workflows/release.yml`
- Modify: `README.md`
- Modify: `DESIGN.md`

- [ ] **Step 1: Publish helper assets on version tags**

Release workflow builds `NapisyPL.LocalTranslator-win-x64.zip` and `.sha256`, then builds the self-contained NapisyPL Windows package. Application version and helper compatibility version must be explicit in manifest metadata.

- [ ] **Step 2: Run fresh verification**

Run:

```bash
python -m unittest tools/local-translator/test_protocol.py -v
dotnet test NapisyPL.sln -c Release
dotnet publish src/NapisyPL/NapisyPL.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/win-x64
```

- [ ] **Step 3: Windows manual smoke test**

On a clean or isolated `%LocalAppData%/NapisyPL` directory: select `Lokalny (Argos)` → install helper/model → disconnect network → translate the benchmark SRT → translate a two-file folder → cancel one run mid-file → reopen app and verify installed state is reused.

- [ ] **Step 4: Document measured results and known limitations**

README must distinguish verified automated tests from manual Windows/model/API tests.

- [ ] **Step 5: Commit**

```bash
git add .github/workflows/release.yml README.md DESIGN.md
git commit -m "build: release NapisyPL with offline translator"
```
