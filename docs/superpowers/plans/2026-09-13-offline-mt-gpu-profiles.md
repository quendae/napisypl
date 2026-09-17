# Offline MT GPU Profiles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three persistent local translation profiles — NLLB 600M Fast, NLLB 1.3B Balanced, and MADLAD-400 3B Quality — with automatic AMD GPU/CPU selection and FP16 on GPU.

**Architecture:** Keep the existing process-isolated Transformers helper and make it profile-aware. Model descriptors select model repository, pinned revision, local directory, licensing, and translation family. A single runtime process keeps the selected model loaded across all translation batches and therefore across the existing folder queue. The helper auto-detects a usable PyTorch GPU, uses FP16 on GPU, falls back to CPU FP32, reports device/dtype/batch metadata, and handles NLLB language tokens vs MADLAD `<2pl>` prompting.

**Tech Stack:** .NET 10, Avalonia, Python 3.12, Transformers 4.57.6, PyTorch, AMD ROCm/TheRock on Windows, Hugging Face model assets, GitHub Actions.

**Spec:** User-approved conversation design: Fast=NLLB distilled 600M, Balanced=NLLB distilled 1.3B, Quality=MADLAD-400 3B; Auto GPU/CPU; keep model resident while translating a folder.

## Global Constraints

- Existing CPU-only NLLB 600M behavior must remain a valid fallback.
- NLLB profiles remain CC-BY-NC-4.0 / benchmark-only.
- MADLAD-400 3B is Apache-2.0 and is not benchmark-only.
- Folder translation must reuse one provider/runtime instance so the model remains loaded between files.
- GPU failures during initialization must fall back to CPU in Auto mode rather than crashing the desktop process.
- Real GPU packaging is optional at runtime; CPU helper remains bundled so every build stays runnable.

---

### Task 1: Profile descriptors and storage paths

**Files:**
- Modify: `src/NapisyPL.Core/OfflineMt/Nllb/NllbModelDescriptor.cs`
- Modify: `src/NapisyPL.Core/OfflineMt/OfflineMtPaths.cs`
- Modify: `tests/NapisyPL.Core.Tests/NllbAssetManagerTests.cs`

**Interfaces:**
- Produces: `NllbModelProfile` enum and descriptors `Fast600M`, `Balanced1_3B`, `QualityMadlad3B`.
- Produces: profile-specific model directories.

- [ ] Write tests asserting backend/model/license/family/path metadata for all three profiles.
- [ ] Run the focused tests and verify they fail before implementation.
- [ ] Implement descriptors and paths while keeping `Pinned` as a compatibility alias for Fast600M.
- [ ] Run focused tests and verify they pass.

### Task 2: Profile-aware model installer/client/runtime registry

**Files:**
- Modify: `src/NapisyPL.Core/OfflineMt/Nllb/NllbAssetManager.cs`
- Modify: `src/NapisyPL.Core/OfflineMt/Nllb/NllbTranslatorClient.cs`
- Modify: `src/NapisyPL.Core/OfflineMt/Nllb/NllbRuntimeRegistry.cs`
- Modify: `tests/NapisyPL.Core.Tests/NllbTranslatorClientTests.cs`

**Interfaces:**
- `NllbAssetManager(..., NllbModelDescriptor descriptor)` installs that exact profile.
- `NllbTranslatorClient(..., NllbModelDescriptor descriptor, ...)` reports profile-specific identity and license.
- `NllbRuntimeRegistry.GetOrCreate(HttpClient, NllbModelProfile)` caches one client per profile.

- [ ] Add failing tests for Fast/Balanced/Quality identity and one-time initialization.
- [ ] Implement descriptor-aware installer/client/registry with backward-compatible constructors where useful.
- [ ] Verify profile tests pass.

### Task 3: Auto GPU/CPU helper with MADLAD support

**Files:**
- Modify: `tools/offline-mt/nllb_helper.py`
- Modify: `tools/offline-mt/test_nllb_protocol.py`
- Modify: `src/NapisyPL.Core/OfflineMt/Nllb/NllbRuntimeManager.cs`
- Modify: `src/NapisyPL.Core/LocalTranslation/LocalTranslatorProtocol.cs` only if ready metadata requires an additive event field.

**Interfaces:**
- Helper detects model family from local `config.json`.
- NLLB: `eng_Latn` source + forced `pol_Latn` BOS.
- MADLAD: prepend `<2pl> ` to each source segment.
- Auto device: use `torch.cuda` when available; GPU uses float16, CPU uses float32.
- Ready/status metadata includes device, dtype, resolved GPU name, and effective batch size.

- [ ] Add protocol tests for CPU fallback, NLLB prompt behavior, MADLAD prompt behavior, and GPU tensor movement using a fake torch/model layer.
- [ ] Verify tests fail on current CPU-only helper.
- [ ] Implement Auto device and model-family translation strategies.
- [ ] Add adaptive batch defaults: Fast 32 GPU / 8 CPU, Balanced 16 GPU / 4 CPU, Quality 8 GPU / 2 CPU, with OOM backoff by halving the batch.
- [ ] Verify Python protocol tests and .NET runtime tests pass.

### Task 4: UI profiles and persistent folder runtime

**Files:**
- Modify: `src/NapisyPL.Core/Translation/ProviderFactory.cs`
- Modify: `src/NapisyPL/MainWindow.Argos.cs`
- Modify: `tests/NapisyPL.Core.Tests/ProviderFactoryTests.cs` if present; otherwise add focused provider-name tests.

**Interfaces:**
- Provider labels: `NLLB 600M — Fast`, `NLLB 1.3B — Balanced`, `MADLAD-400 3B — Quality`.
- Existing `FolderBatchService` continues using the one provider created before `TranslateFolderAsync`, so the selected runtime remains resident for the whole queue.

- [ ] Add failing provider mapping tests.
- [ ] Wire each label to its profile and update hints/licensing text.
- [ ] Verify folder translation creates the provider once and does not dispose it between files.
- [ ] Run focused and full .NET tests.

### Task 5: Windows AMD GPU runtime bootstrap/package validation

**Files:**
- Create: `tools/offline-mt/install-amd-runtime.ps1`
- Modify: `.github/workflows/package-nllb.yml`
- Modify: `tools/offline-mt/requirements-nllb.txt`
- Add/modify smoke tooling under: `tools/offline-mt/NllbSmoke/**`

**Interfaces:**
- Stable AMD ROCm/TheRock index: `https://stable.repo.amd.com/rocm/whl-next/`.
- gfx1030 package target for RX 6950 XT.
- Main Windows package always contains CPU fallback helper; GPU runtime can be installed side-by-side and selected automatically when present.

- [ ] Add a Windows smoke that reports device/dtype and translates the semantic probe.
- [ ] Add a runtime install script for `torch[device-gfx1030]` without torchvision/torchaudio.
- [ ] Keep the ordinary CPU package build green on hosted CI where no AMD GPU exists.
- [ ] Produce a Windows test build and verify desktop startup plus real Fast-model translation.

### Task 6: Verification and handoff

- [ ] Run all .NET tests.
- [ ] Run Python protocol tests.
- [ ] Run real-model Windows smoke for NLLB 600M.
- [ ] Confirm Fast/Balanced/Quality model downloads are pinned and manifests report the correct license.
- [ ] Confirm folder mode reuses one runtime process across multiple files.
- [ ] Package a test ZIP/release and document expected first-run downloads and AMD runtime setup.