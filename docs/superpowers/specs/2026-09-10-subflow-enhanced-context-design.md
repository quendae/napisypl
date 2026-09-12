# SubFlow Enhanced Context Design

Date: 2026-09-10
Status: approved direction

## Goal

Add an optional fully-local Enhanced mode that improves English→Polish grammatical gender and addressee choices without requiring a cloud LLM and without retranscribing the whole movie.

## Product modes

- Standard: translate subtitles only.
- Enhanced: subtitles + audio speaker diarization + local semantic context resolver + translation + targeted gender/context correction.

Enhanced must remain optional. Standard translation continues to work without audio analysis or the local resolver model.

## Pipeline

1. Extract/read text subtitles and timestamps.
2. Extract a lightweight mono 16 kHz audio stream only when Enhanced is enabled.
3. Run speaker diarization and map subtitle cues to stable speaker IDs such as SPEAKER_01.
4. Run cheap deterministic heuristics first (pronouns, titles, explicit relations, ASS/WebVTT speaker metadata, obvious two-speaker turn-taking).
5. Send only unresolved/low-confidence context windows to a small local LLM resolver.
6. Resolver returns structured metadata only; it does not translate subtitle prose.
7. Translation provider translates subtitle text.
8. Run a targeted review pass only for lines whose Polish inflection can depend on speaker/addressee gender or number.
9. Write ordinary SRT/TXT output.

## Local context resolver

Default candidate: Qwen3-1.7B GGUF Q4_K_M running via llama.cpp.

Reasons:
- small enough for 16 GB RAM systems;
- model file is roughly 1.3 GB;
- multilingual instruction following;
- Apache-2.0 model license;
- llama.cpp has straightforward Windows binaries and GGUF support;
- use non-thinking mode for low latency and bounded output.

The resolver process is separate from the Avalonia GUI. The model is downloaded only when Enhanced is enabled for the first time.

Suggested local layout:

%LocalAppData%\SubFlow\context-resolver\
  runtime\llama-server.exe
  models\qwen3-1.7b-q4_k_m.gguf
  logs\

The process binds to localhost only or communicates over stdio. It must never be exposed to the LAN.

## Resolver input

The resolver receives short windows, normally 20–60 subtitle cues. Each cue contains:
- cue id;
- speaker id when known;
- source English text;
- optional explicit speaker label from ASS/WebVTT;
- deterministic hints already found by heuristics.

It must not receive video frames in Enhanced v1.

## Resolver output

Strict JSON, for example:

{
  "speakers": {
    "SPEAKER_01": { "gender": "female", "confidence": 0.96 },
    "SPEAKER_02": { "gender": "male", "confidence": 0.91 }
  },
  "lines": {
    "118": { "addressee": "SPEAKER_01", "confidence": 0.83 },
    "119": { "addressee": "SPEAKER_02", "confidence": 0.78 }
  }
}

Allowed gender values: male, female, mixed, unknown.
Addressee may be a speaker ID, group, unknown, or omitted.

## Conservative correction rule

Enhanced must not rewrite every translated line. It should identify Polish constructions likely to encode gender/number and revise only those lines when context confidence is sufficient.

Examples include past-tense verbs and predicative adjectives:
- byłem/byłam
- zrobiłeś/zrobiłaś
- gotowy/gotowa
- zmęczony/zmęczona

If confidence is low, preserve the original translation rather than guessing.

## Caching

For a movie, cache the context map keyed by source file fingerprint + subtitle-track identity. Re-running with another translation provider should reuse diarization/context results and avoid reprocessing audio.

## Progress

Enhanced exposes independent progress phases:
- audio extraction;
- speaker analysis;
- context resolution;
- translation;
- context/gender review;
- output.

The UI must never look frozen while llama.cpp is generating. Log model load time, prompt token count if available, generated token count, request duration, and exit/error category. Do not log subtitle text by default.

## Failure behavior

If audio extraction, diarization, or resolver fails, offer/use Standard translation rather than losing the whole job. Enhanced errors are recorded in diagnostics.

## Future

- optional 4B resolver for higher accuracy;
- speaker/name persistence across TV episodes where reliable;
- sampled video-frame analysis as an experimental Maximum mode;
- benchmark 1.7B vs 4B resolver on manually reviewed subtitle sets.
