# SubFlow Quality Model + Deterministic Enhanced Design

Date: 2026-09-13
Status: proposed, user-approved in chat; written checkpoint before implementation
Branch: `feature/offline-mt-gpu-profiles`

## 1. Problem statement

SubFlow now has a working AMD-GPU offline translation path and three tested MT profiles:

- NLLB-200 distilled 600M (Fast)
- NLLB-200 distilled 1.3B (Balanced)
- MADLAD-400 3B (Quality)

User-side comparison shows a meaningful quality gain from 600M to larger dedicated MT models. MADLAD 3B is currently the best local overall result, but NLLB 1.3B handled some difficult phrases better than MADLAD. Therefore the full NLLB-200 3.3B model should be tested directly before deciding the final Quality model.

At the same time, the existing Enhanced feature still reflects an older architecture: it exposes a second local LLM reviewer/backend and asks that model to propose grammatical gender corrections. This is now unnecessary and introduces a risk that a reviewer rewrites more of the subtitle than intended.

## 2. Goals

1. Add NLLB-200 3.3B as an experimental quality profile alongside MADLAD 3B.
2. Keep all existing GPU/CPU fallback behavior and model residency for folder batches.
3. Redefine Enhanced as a single optional context/gender-correction switch.
4. Remove the second reviewer LLM from the Enhanced correction path.
5. Drive corrections from audio diarization, voice-gender evidence and dialogue addressee resolution.
6. Apply only deterministic, bounded Polish grammatical-gender edits.
7. Prefer no change over an uncertain change.

## 3. Non-goals

- Do not remove Argos/Bergamot/Marian implementation code in this change.
- Do not build a complete Polish morphological analyzer.
- Do not attempt general style rewriting or semantic correction in Enhanced.
- Do not infer gender from names or stereotypes.
- Do not modify lines when dialogue target or gender evidence is ambiguous.
- Do not replace MADLAD with NLLB 3.3B before a direct user-side comparison.

## 4. Model profiles

The offline MT profile set becomes:

- `Fast600M` -> `facebook/nllb-200-distilled-600M`
- `Balanced1_3B` -> `facebook/nllb-200-distilled-1.3B`
- `QualityNllb3_3B` -> `facebook/nllb-200-3.3B`
- `QualityMadlad3B` -> `google/madlad400-3b-mt`

### NLLB 3.3B runtime defaults

- GPU dtype: FP16
- Initial GPU batch: 4
- Adaptive fallback: 4 -> 2 -> 1 on OOM
- CPU fallback remains available but is expected to be very slow and should be clearly reported in status/logging
- Model remains loaded for the entire folder queue

No speed claim is made until tested on the RX 6950 XT.

## 5. Enhanced user experience

Enhanced becomes a single checkbox/switch in the main translation UI.

User-facing meaning:

> Enhanced context & gender correction — uses the video's audio and dialogue turn-taking to correct only grammatical gender when confidence is high.

The existing user-facing controls for reviewer model and reviewer backend are removed from the normal Enhanced flow. Internal audio/diarization/runtime details may still be logged for diagnostics.

Enhanced is applicable only when the input has usable audio. Subtitle-only inputs fall back to Standard translation with a clear status message.

## 6. Enhanced processing pipeline

The pipeline is:

`source subtitles -> MT translation -> audio diarization -> cue/speaker mapping -> voice-gender evidence -> dialogue addressee resolution -> deterministic gender fix -> output`

Translation remains entirely the responsibility of the selected MT provider. Enhanced does not ask another model to rewrite translations.

### Why analysis happens after translation

The correction engine needs both:

- source dialogue timing/speaker structure,
- the translated Polish text that may contain a gendered form.

Audio analysis can be started/warmed in parallel where practical, but the actual deterministic edit is applied to the completed translated cue list.

## 7. Addressee scenario

The primary required scenario is:

1. Cue A is spoken by speaker M.
2. Cue B follows shortly and is spoken by speaker F.
3. Dialogue resolver determines with high confidence that F is the addressee of cue A.
4. Voice-gender evidence for F is confidently female.
5. The Polish translation of cue A contains a masculine second-person form.
6. Enhanced changes only that form to the feminine equivalent.

Example:

`Zrobiłeś to?` -> `Zrobiłaś to?`

The reverse direction must also work.

Existing resolver behavior such as `next_turn_two_speaker`, sandwich turns, persistent two-speaker dialogue, third-speaker rejection and monologue no-op remains authoritative.

## 8. Deterministic Polish gender fixer

### 8.1 Inputs

For every candidate cue the fixer receives:

- translated cue text,
- current speaker id and speaker gender evidence,
- probable addressee id and addressee gender evidence,
- resolver confidence/reason,
- correction target: speaker or addressee.

### 8.2 Output

The fixer returns either:

- unchanged cue, or
- a cue with one or more narrowly bounded inflection-only replacements.

It must not return rewritten full sentences.

### 8.3 Safe rule families

Initial rules cover common subtitle forms with high precision:

#### First-person singular speaker agreement

Examples:

- `zrobiłem <-> zrobiłam`
- `byłem <-> byłam`
- `chciałem <-> chciałam`
- `mógłbym <-> mogłabym`

High-value suffix families include forms corresponding to `-łem/-łam`, `-bym/-abym` and related bounded variants already partially represented in existing gender-edit guards.

#### Second-person singular addressee agreement

Examples:

- `zrobiłeś <-> zrobiłaś`
- `byłeś <-> byłaś`
- `chciałeś <-> chciałaś`
- `mógłbyś <-> mogłabyś`

High-value suffix families include `-łeś/-łaś`, `-byś/-abyś` and related bounded variants.

#### Plural agreement

Support only forms for which the current resolver/evidence can distinguish the required masculine-personal vs non-masculine-personal direction safely, for example selected `-li/-ły`, `-liśmy/-łyśmy`, `-liście/-łyście` patterns.

#### Small adjective/participle set

Only high-confidence forms with explicit tested transformations are included initially. This set expands from real subtitle failures rather than trying to cover all Polish morphology upfront.

### 8.4 Irregular lexicon

A compact explicit lexicon handles forms unsafe for suffix-only rewriting.

Initial motivating example:

- `wyszłaś <-> wyszedłeś`

The lexicon is intentionally small and test-driven. New entries are added when real subtitle comparisons expose a recurring safe pair.

### 8.5 Casing and punctuation

The fixer must preserve:

- original capitalization pattern where possible,
- punctuation,
- subtitle markup,
- surrounding whitespace.

Matching operates on word boundaries and bounded phrase spans, never blind global string replacement.

## 9. Safety gates

A gender edit is allowed only when all relevant gates pass.

### Speaker-target edit

- current speaker id known,
- speaker gender evidence eligible and confident,
- source form direction conflicts with detected speaker gender,
- transformation is in the safe rule/lexicon set.

### Addressee-target edit

- probable addressee resolved,
- resolver confidence meets the existing strict threshold,
- addressee differs from current speaker,
- addressee gender evidence eligible and confident,
- no third-speaker ambiguity,
- transformation is in the safe rule/lexicon set.

### Reject/no-op cases

No edit when:

- speaker/addressee unknown,
- gender evidence is weak/unknown,
- more than two local speakers make addressee ambiguous,
- cue is a monologue with no reliable responder,
- explicit third-person subject would make a second-person edit unsafe,
- phrase appears multiple times ambiguously,
- transformation is not whitelisted,
- target classification is unclear.

## 10. Reuse of existing code

The redesign should reuse, simplify or replace pieces already present rather than starting from scratch:

- `DialogueAddresseeResolver` remains the source of addressee inference.
- `SpeakerVoiceGenderService` and `SpeakerGenderEvidence` remain the acoustic evidence layer.
- Existing safety ideas in `SurgicalGenderEditProtocol`, `SpeakerGenderEditDirectionGuard`, `GenderAgreementTargetClassifier` and `SurgicalGenderEditApplier` are retained where they make sense.
- `LocalTargetedGenderReviewService` is removed from the active Enhanced correction path.
- The reviewer LLM runtime is no longer required merely to use Enhanced.

Implementation may introduce a dedicated `DeterministicGenderFixer`/`PolishGenderFixer` class rather than forcing rule generation through the existing LLM response protocol.

## 11. Logging and diagnostics

Enhanced logs should report counts, not sensitive subtitle contents by default:

- candidate cues,
- resolved addressee candidates,
- known gender evidence count,
- applied edits,
- skipped edits grouped by reason,
- ambiguous/third-speaker/no-evidence rejections.

For NLLB 3.3B, runtime logs should include:

- model profile,
- device,
- dtype,
- batch size,
- OOM fallback events,
- model load/warm-up duration,
- translation duration/throughput.

## 12. Testing strategy

### Model profile tests

- descriptor points to the pinned NLLB 3.3B model/revision,
- profile appears in UI/provider composition,
- helper receives correct model kind/source/target metadata,
- GPU batch starts at 4 and backs off deterministically on simulated OOM,
- CPU fallback remains intact.

### Deterministic fixer tests

At minimum:

- masculine -> feminine first person,
- feminine -> masculine first person,
- masculine -> feminine second person,
- feminine -> masculine second person,
- conditional forms,
- irregular lexicon pair,
- capitalization preservation,
- punctuation/markup preservation,
- repeated-word ambiguity no-op,
- third-person conflict no-op,
- low-confidence gender no-op,
- monologue no-op,
- third-speaker ambiguity no-op.

### User-scenario regression

Preserve and adapt the existing test that proves:

- male current speaker,
- next responder classified female,
- resolver chooses the female responder as addressee,
- masculine second-person Polish form becomes feminine.

Also preserve the monologue test where no edit is made.

## 13. Packaging and user validation

CI can validate code, model metadata, helper protocol and CPU smoke where practical, but cannot prove AMD GPU execution.

Final validation requires the RX 6950 XT test machine:

1. translate the same episode with NLLB 3.3B,
2. record total time, warm throughput, peak VRAM if available and runtime batch,
3. compare SRT quality directly with MADLAD 3B, NLLB 1.3B and DeepL,
4. run the same material with Enhanced on/off,
5. inspect known gender-error examples and verify that only intended inflections change.

The final Quality default is chosen only after this comparison.

## 14. Rollback / compatibility

- Standard mode remains unchanged when Enhanced is off.
- Existing Fast/Balanced/MADLAD profiles continue to work.
- Old reviewer/Argos/Bergamot code is not deleted in this change, allowing rollback and later cleanup.
- If deterministic Enhanced encounters an unsupported form, output remains the original MT translation rather than attempting a speculative rewrite.
