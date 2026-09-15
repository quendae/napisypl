# QNapi subtitle acquisition implementation plan

**Goal:** Search for Polish subtitles for video files; otherwise download English subtitles and run the existing translator and optional audio gender correction.

**Architecture:** Use QNapi as a separate CLI engine with explicitly isolated language passes and temporary output. Keep acquisition separate from translation. Pass downloaded cues together with the original video path to Standard/Enhanced. Preserve current manual translation when search is disabled.

**Tech stack:** .NET 10, Avalonia, xUnit, external QNapi executable.

## Acceptance and constraints

- Polish results bypass translation/model initialization; English results preserve cue timing and video/audio context.
- File and folder flows must accept videos without embedded text subtitles while search is enabled.
- Separate PL and EN requests must not silently accept the user's configured fallback language.
- QNapi must not overwrite user files or modify its global settings. Cancellation and timeout must stop its process and clean owned temporary files.
- Validate downloaded subtitles before saving. Existing Polish output is preserved in acquisition mode.
- Keep optional local/external translator settings and gender algorithms unchanged.
- Do not copy GPL implementation into the C# application. Record the external dependency and setup instructions accurately.

## Tasks

- [x] Inspect local repo, baseline tests, QNapi source/protocol and CLI.
- [x] Add a video-plus-cues translation interface, shared by Standard and Enhanced; regression tests for preserving video context and timing.
- [x] Add QNapi adapter and runtime discovery/setup; test isolated language arguments, output validation, cancellation and no-result behavior.
- [x] Add acquisition pipeline: PL → EN → embedded fallback; test bypass, cancellation, existing output, errors, disabled search.
- [x] Wire controls/settings, lazy translator creation, folder processing and accurate status messages.
- [x] Build application, run tests, review integrated changes, document limits and usage.

## Baseline evidence

HEAD before work: cf36db4. Working tree initially clean. Local .run/dotnet/dotnet.exe is SDK 10; system dotnet is SDK 8.
Baseline test suite: 183 passed, one Windows DPAPI round-trip failed in sandbox before changes.

## Decisions

User explicitly authorized implementation and model-routed parallel work. Use a local feature branch and bounded agents; no push/merge requested.
QNapi console reuse is preferable to a C++ rewrite: preserves provider logic without maintaining another copy of legacy protocols.
Named web/game/video-production plugins do not apply to this Avalonia desktop feature; repository research uses the selected GitHub connector.
The official Windows portable archive contains `qnapi.exe`, not `qnapic.exe`. The GUI binary itself accepts quiet download arguments; `-c` is intentionally omitted because it requires the absent console binary.
