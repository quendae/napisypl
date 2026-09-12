# SubFlow Bergamot helper

`SubFlow.BergamotHelper.exe` is a small persistent JSON Lines wrapper around the native `browsermt/bergamot-translator` runtime.

Pinned build inputs:

- Bergamot: `browsermt/bergamot-translator` tag `v0.4.5` (MPL-2.0).
- JSON parser: `nlohmann/json` commit `9cca280a4d0ccf0c08f47a99aa71d1b0e52f8d03` / v3.11.3 (MIT).

The helper is built on Windows x64 in `.github/workflows/offline-mt-runtime.yml`. The helper directory is copied into the pinned upstream Bergamot checkout and added as a CMake subdirectory, so it links directly against the upstream `bergamot-translator` target.

Protocol:

- input: `load`, `translate`, `shutdown` JSON objects, one per line;
- output: `ready`, `segment`, `progress`, `complete`, `error` JSON objects, one per line;
- `load.modelPath` can be a Bergamot `config.yml` path or a directory containing `config.yml`;
- source/translated subtitle text is never written to stderr by the wrapper; Bergamot logging is configured as `off`.

The runtime is CPU-only for the benchmark build. `BUILD_ARCH=x86-64-v2` is used for a conservative Windows x64 binary instead of tuning the helper to the GitHub runner CPU.
