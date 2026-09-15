# Subtitle synchronization and alternative selection design

## Objective

Prefer usable Polish subtitles over local machine translation. Compare every Polish candidate with an English text track embedded in the video when one is available, correct only well-supported timing differences, and offer an interactive QNapi or local SRT fallback when the automatic QNapi result is missing or unsafe. Translate English only after Polish sources are exhausted.

## Evidence and chosen policy

The Better Call Saul S04E10 sample contains 802 embedded English cues, 802 machine-translated Polish cues with identical timing, and 768 downloaded Polish cues. A robust fit maps downloaded Polish timing to the embedded English timeline with scale `1.0` and an offset of about `-100 ms`; the median residual is 67 ms and the 90th percentile is 193 ms. There is no FPS drift. Cue indexes cannot be paired directly because human subtitles merge and omit lines, and provider credits extend the downloaded file beyond the dialogue.

The program therefore treats subtitle timing as two event timelines. It estimates a linear transform `referenceTime = candidateTime * scale + offset` from nearest cue boundaries, rejects outliers, and uses coverage plus residual error to decide whether the candidate is already aligned, safe to synchronize, needs user review, or must be rejected.

## User-visible acquisition flow

1. Keep an existing `<video>.pl.srt` without overwriting it.
2. When the selected embedded track is textual, extract it once and use its cues as the timing reference and preferred English translation source.
3. Search QNapi automatically for Polish subtitles.
4. If Polish cues and an embedded reference exist, analyze them before saving:
   - aligned: save without changing timestamps;
   - safe transform: apply the transform and save;
   - uncertain or rejected: do not silently save them.
5. If the automatic Polish result is absent or unsafe in the single-file UI, let the user run interactive QNapi or choose a local SRT. Analyze the returned file by the same rules. The user may explicitly accept an uncertain candidate after seeing its timing summary.
6. Folder processing never opens modal dialogs. It rejects unsafe Polish candidates and continues to the English fallback.
7. When no usable Polish candidate remains, translate the embedded English track if present. Only when it is absent, search QNapi for English and translate the downloaded English cues.
8. If neither Polish nor English subtitles exist, return an actionable error.

## Timing analysis

`SubtitleSynchronizationService` is pure and consumes two validated cue lists. It uses cue start and end boundaries, not cue numbers or text equality. It searches the standard FPS ratios near 1.0 and a bounded affine fit, calculates a robust offset from matched nearest boundaries, then removes matches outside the residual window and recalculates the fit.

The analysis reports scale, offset, matched-cue coverage, median residual, 90th-percentile residual, and a decision:

- `Aligned`: scale differs from 1 by at most 0.1%, absolute offset is at most 250 ms, coverage is at least 75%, and P90 residual is at most 350 ms.
- `SafeToSynchronize`: coverage is at least 75%, P90 residual is at most 350 ms, scale is within 0.5% of either 1.0 or a known FPS ratio, and the transformed timeline remains valid.
- `NeedsReview`: coverage is at least 50% and P90 residual is at most 750 ms.
- `Rejected`: anything weaker, empty input, or a transform that produces negative or reversed cue times.

The analyzer ignores unmatched leading/trailing cues, including provider credits. For `Aligned`, timestamps are kept unchanged to avoid rewriting a harmless 100 ms difference. For `SafeToSynchronize`, every cue start and end is transformed, clamped to zero, and renumbered in stable order.

## Alternative subtitle boundary

The core pipeline accepts an optional `ISubtitleAlternativeSelector`. It receives the video path, desired language, the rejected automatic candidate and its timing analysis, and whether an embedded reference exists. It returns either a parsed candidate plus an explicit user choice (`ApplyRecommendedTransform`, `UseWithoutChanges`, or `Cancel`) or no candidate.

The Avalonia implementation shows the timing summary and offers:

- run QNapi without quiet mode so its own selection window can offer provider alternatives;
- select a local `.srt` file;
- continue to English translation.

The QNapi adapter owns its temporary sidecar and deletes it after parsing in automatic and interactive modes. Local SRT files remain untouched. The selector is disabled for folder batches.

## Safety and diagnostics

- Never overwrite an existing Polish output.
- Validate cue text, unique indexes, non-negative starts and positive durations before analysis or writing.
- Preserve cancellation and QNapi timeouts.
- Log only provider, decision, cue counts, scale, offset, coverage, and residuals; do not log subtitle text.
- Do not change the gender resolvers, translation providers, or MadLad configuration in this slice.

## MadLad and idioms

MadLad-400 3B remains the default local last-resort translator. The observed 802-cue run completed without an out-of-memory failure on the user's 16 GB GPU. Human Polish subtitles remain preferable because they are shorter and more idiomatic. General idiom expansion is a separate feature: it should use a small source-anchored lexicon with regression tests rather than global Polish string replacement.

