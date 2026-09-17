# SubFlow local MT — status and next plan

Date: 2026-09-13
Branch: `feature/offline-mt-gpu-profiles`

## Current working direction

SubFlow is moving away from Argos/Bergamot as the primary offline translation path. The current local translation stack is based on dedicated neural MT models running through PyTorch/Transformers, with AMD GPU acceleration on RX 6950 XT / `gfx1030` through the private TheRock runtime.

The current tested profiles are:

- **Fast — NLLB-200 distilled 600M**
- **Balanced — NLLB-200 distilled 1.3B**
- **Quality — MADLAD-400 3B**

A fourth profile will now be added for direct quality comparison:

- **Quality Test — NLLB-200 3.3B**

The goal is to compare NLLB 3.3B and MADLAD 3B on the same subtitle material before deciding which model should remain the default Quality profile.

Detailed design checkpoint: `docs/superpowers/specs/2026-09-13-subflow-quality-and-deterministic-enhanced-design.md`.

## RX 6950 XT test results so far

User-side testing on an RX 6950 XT confirms that the AMD GPU runtime works and that keeping the model resident in VRAM is worthwhile.

Observed full-run timings for the same ~685-segment episode were approximately:

| Profile | Full run | Warm throughput / practical observation |
| --- | ---: | --- |
| NLLB 600M Fast | ~23.8 s | ~43 seg/s after warm-up |
| NLLB 1.3B Balanced | ~133 s | ~14 seg/s after warm-up |
| MADLAD-400 3B Quality | ~379 s | ~6.1 seg/s after warm-up |

The larger models spend a large part of the first run loading/warming the model. Subsequent work is much faster, therefore folder translation should keep one model loaded for the whole queue instead of unloading it between files.

## Quality observations

On the tested Better Call Saul episode the practical ranking was:

`DeepL > MADLAD 3B > NLLB 1.3B > NLLB 600M`

However, NLLB 1.3B sometimes handled difficult idiomatic fragments better than MADLAD 3B, which justifies testing the full NLLB 3.3B model before locking the Quality choice.

All tested local MT models still make dialogue-gender mistakes when English itself does not encode the target-language gender. Increasing translation model size alone does not reliably solve this problem.

## Enhanced direction change

The old Enhanced concept combined translation with a second local LLM reviewer. That is no longer the preferred architecture.

**New Enhanced goal:** a single optional switch that performs only context/gender correction after translation.

Pipeline:

`MT model -> audio diarization -> voice-gender evidence -> dialogue addressee resolver -> deterministic Polish gender fixer -> output`

The translation itself remains the output of NLLB/MADLAD. Enhanced must not freely rewrite the subtitle text.

### User scenario to preserve

If a male speaker says a phrase whose Polish translation addresses a male person, but the next responding speaker is confidently detected as female and the dialogue resolver identifies her as the addressee, Enhanced should change only the grammatical gender required by that addressee.

Example:

`Zrobiłeś to?` -> `Zrobiłaś to?`

The inverse direction must work as well. If there is no reliable addressee (for example a monologue, third speaker ambiguity, weak gender evidence or a large dialogue gap), Enhanced must leave the text unchanged.

There is already regression coverage for this exact turn-taking scenario in `TurnTakingGenderUserScenarioTests` and addressee-resolution logic in `DialogueAddresseeResolver`.

## Deterministic fixer instead of reviewer LLM

The second reviewer LLM will be removed from the active Enhanced flow. The deterministic fixer will reuse the safety concepts already present in `SurgicalGenderEditProtocol` and related guards, but it will generate edits itself instead of asking an LLM to propose them.

Initial supported families should cover high-value subtitle cases:

- 1st person past tense: `-łem <-> -łam`, including related conditional forms,
- 2nd person past tense: `-łeś <-> -łaś`, including related conditional forms,
- plural personal/non-masculine-personal forms where the resolver has strong evidence,
- adjective/participle agreement in a deliberately small safe set,
- a compact irregular lexicon for forms that cannot be handled safely by suffix replacement (for example `wyszłaś <-> wyszedłeś`).

Rules must preserve capitalization and punctuation and operate only on bounded word/phrase spans. Unknown or ambiguous cases are skipped.

## Immediate implementation plan

1. Add `NLLB-200 3.3B` as an experimental profile without replacing MADLAD.
2. Start the 3.3B GPU profile conservatively (FP16, batch 4) and keep existing adaptive OOM fallback.
3. Expose it in the same offline MT provider/profile UI and logging used by the other models.
4. Keep the model resident during folder translation.
5. Simplify Enhanced UI to one switch; remove user-facing reviewer model/backend selection from the normal flow.
6. Replace the LLM gender-review step with a deterministic rule engine driven by speaker/addressee gender evidence.
7. Preserve the existing conservative resolver guards: next responder, two-speaker context, confidence threshold, third-speaker rejection and monologue no-op behavior.
8. Add regression tests for masculine<->feminine transformations, irregular forms, capitalization, third-person safety and ambiguous/no-op cases.
9. Run .NET tests and package/runtime smoke tests.
10. Produce a Windows test ZIP for RX 6950 XT and compare NLLB 3.3B with MADLAD 3B on the same SRT/episode.

## Deferred cleanup

Argos and Bergamot/Marian code can remain in the repository temporarily so this quality work does not become a destructive cleanup at the same time. They are no longer on the critical path and should not shape Enhanced architecture.

Once the Quality model and deterministic Enhanced path are validated, a separate cleanup pass can retire obsolete provider/UI/runtime code safely.
