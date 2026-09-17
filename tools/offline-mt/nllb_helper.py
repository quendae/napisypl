from __future__ import annotations

import gc
import importlib.metadata
import os
import json
import pathlib
import re
import sys
from collections import Counter
from collections.abc import Callable, Sequence

SOURCE_LANGUAGE = "eng_Latn"
TARGET_LANGUAGE = "pol_Latn"
MADLAD_TARGET_PREFIX = "<2pl> "
DEFAULT_RUNTIME_VERSION = "transformers-4.57.6"

_WORD_RE = re.compile(r"\w+", re.UNICODE)
_NUMBER_RE = re.compile(r"(?<!\w)\d+(?!\w)")
_DEGENERATE_SYMBOLS = "*#~|=_♪"

_translate_texts: Callable[[Sequence[str]], list[str]] | None = None
_model_name = "unknown"
_runtime_version = DEFAULT_RUNTIME_VERSION
_device_name = "CPU"
_dtype_name = "float32"
_batch_size = 8


class RuntimeDependencyError(RuntimeError):
    pass


def configure_stdio() -> None:
    for stream in (sys.stdin, sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="strict")


def emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def emit_diagnostic(payload: dict) -> None:
    sys.stderr.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stderr.flush()


def infer_model_family(config: dict) -> str:
    model_type = str(config.get("model_type", "")).strip().lower()
    if model_type in {"m2m_100", "nllb_moe"}:
        return "nllb"
    if model_type in {"t5", "mt5"}:
        return "madlad"
    raise RuntimeError(f"unsupported offline MT model type: {model_type or 'missing'}")


def prepare_source_texts(family: str, texts: Sequence[str]) -> list[str]:
    if family == "nllb":
        return list(texts)
    if family == "madlad":
        return [MADLAD_TARGET_PREFIX + text for text in texts]
    raise RuntimeError(f"unsupported offline MT model family: {family}")


def _word_tokens(text: str) -> list[str]:
    return [token.casefold() for token in _WORD_RE.findall(text)]


def _longest_consecutive_numeric_run(values: Sequence[int]) -> int:
    if not values:
        return 0
    best = 1
    current = 1
    for previous, value in zip(values, values[1:]):
        if value == previous + 1:
            current += 1
            best = max(best, current)
        else:
            current = 1
    return best


def _has_repeated_ngram_loop(words: Sequence[str], source_word_count: int) -> bool:
    if len(words) < 12 or len(words) <= (source_word_count * 3) + 12:
        return False

    for ngram_size in range(2, 6):
        required_words = ngram_size * 6
        for start in range(0, len(words) - required_words + 1):
            first = list(words[start : start + ngram_size])
            repeats = 1
            while start + ((repeats + 1) * ngram_size) <= len(words):
                offset = start + (repeats * ngram_size)
                if list(words[offset : offset + ngram_size]) != first:
                    break
                repeats += 1
            if repeats >= 6:
                return True
    return False


def detect_degenerate_output(source_text: str, translated_text: str) -> str | None:
    source_text = source_text or ""
    translated_text = translated_text or ""

    for symbol in _DEGENERATE_SYMBOLS:
        output_count = translated_text.count(symbol)
        source_count = source_text.count(symbol)
        if output_count >= 24 and output_count >= source_count + 16:
            return "symbol_run"

    output_numbers = [int(value) for value in _NUMBER_RE.findall(translated_text)]
    source_numbers = _NUMBER_RE.findall(source_text)
    if (
        len(output_numbers) >= 12
        and len(output_numbers) >= len(source_numbers) + 8
        and _longest_consecutive_numeric_run(output_numbers) >= 12
    ):
        return "numeric_run"

    source_words = _word_tokens(source_text)
    output_words = _word_tokens(translated_text)
    if len(output_words) >= 24:
        _, dominant_count = Counter(output_words).most_common(1)[0]
        dominant_ratio = dominant_count / len(output_words)
        if (
            dominant_count >= 16
            and dominant_ratio >= 0.65
            and len(output_words) > (len(source_words) * 3) + 12
        ):
            return "dominant_token"

    if _has_repeated_ngram_loop(output_words, len(source_words)):
        return "repeated_ngram"

    if len(output_words) > max(96, (len(source_words) * 8) + 32):
        return "length_explosion"

    return None


def length_batching_order(texts: Sequence[str]) -> list[int]:
    """
    Indices of `texts` sorted by length. Batches are padded to their longest
    member, so grouping similar lengths removes padding waste.
    """
    return sorted(range(len(texts)), key=lambda index: len(texts[index]))


def restore_original_order(outputs: Sequence[str], order: Sequence[int]) -> list[str]:
    """Inverse of :func:`length_batching_order`; `outputs` is in sorted order."""
    if len(outputs) != len(order):
        raise RuntimeError("Offline MT runtime returned a mismatched batch.")
    restored = [""] * len(order)
    for position, original_index in enumerate(order):
        restored[original_index] = outputs[position]
    return restored


def default_beam_count() -> int:
    """
    Beam search stays the default. Measured on 20 real cues, greedy decoding was
    about 1.8x faster per batch but no better, and dropped content in a few places
    beam search kept. The large slowdown seen earlier came from degenerate loops
    burning the whole token budget, not from the beams. Override with
    SUBFLOW_MT_BEAMS to trade quality for speed.
    """
    raw = os.environ.get("SUBFLOW_MT_BEAMS", "").strip()
    if raw.isdigit() and 1 <= int(raw) <= 8:
        return int(raw)
    return 4


def bounded_max_new_tokens(source_text: str) -> int:
    """
    A subtitle cue is a sentence, not a paragraph. A flat 256-token ceiling only
    ever binds on a degenerate run, where it lets the model burn the full budget
    before anything notices.
    """
    source_word_count = max(1, len(_word_tokens(source_text)))
    return max(48, min(256, (source_word_count * 5) + 24))


def generation_kwargs(
    target_language_id: int | None,
    source_text: str,
    *,
    retry: bool,
) -> dict:
    kwargs: dict[str, object] = {
        "max_new_tokens": bounded_max_new_tokens(source_text),
        "num_beams": default_beam_count(),
    }
    if kwargs["num_beams"] != 1:
        kwargs["early_stopping"] = True
    if retry:
        source_word_count = max(1, len(_word_tokens(source_text)))
        kwargs.update(
            {
                "max_new_tokens": max(32, min(128, (source_word_count * 4) + 16)),
                "no_repeat_ngram_size": 3,
                "repetition_penalty": 1.15,
                "early_stopping": True,
                # A greedy pass that looped will loop again; searching gives the
                # retry a genuinely different shot at the segment.
                "num_beams": 4,
            }
        )
    if target_language_id is not None:
        kwargs["forced_bos_token_id"] = target_language_id
    return kwargs


def default_batch_size(family: str, model_size_bytes: int, gpu: bool) -> int:
    if family == "nllb" and model_size_bytes >= 15_000_000_000:
        return 4 if gpu else 1
    if family == "madlad" or model_size_bytes >= 9_000_000_000:
        return 8 if gpu else 2
    if model_size_bytes >= 4_000_000_000:
        return 16 if gpu else 4
    return 32 if gpu else 8


def model_weight_size(model_directory: pathlib.Path) -> int:
    total = 0
    for pattern in ("*.bin", "*.safetensors"):
        for path in model_directory.glob(pattern):
            if path.is_file():
                total += path.stat().st_size
    return total


def gpu_is_available(torch_module) -> bool:
    try:
        return bool(torch_module.cuda.is_available())
    except Exception:
        return False


def clear_gpu_cache(torch_module) -> None:
    gc.collect()
    try:
        torch_module.cuda.empty_cache()
    except Exception:
        pass


def load_model(model_path: str) -> None:
    global _translate_texts, _model_name, _runtime_version
    global _device_name, _dtype_name, _batch_size

    model_directory = pathlib.Path(model_path) if model_path else None
    if model_directory is None or not model_directory.is_dir():
        raise FileNotFoundError("Offline MT model directory was not found.")

    config_path = model_directory / "config.json"
    if not config_path.is_file():
        raise FileNotFoundError("Offline MT model config.json was not found.")
    with config_path.open("r", encoding="utf-8") as stream:
        config = json.load(stream)
    if not isinstance(config, dict):
        raise RuntimeError("Offline MT config.json must contain an object.")
    family = infer_model_family(config)

    try:
        import torch
        from transformers import AutoModelForSeq2SeqLM, AutoTokenizer
    except ImportError as exc:
        raise RuntimeDependencyError(
            "Offline MT Python runtime dependencies are not installed."
        ) from exc

    tokenizer_kwargs = {"local_files_only": True}
    if family == "nllb":
        tokenizer_kwargs["src_lang"] = SOURCE_LANGUAGE
    tokenizer = AutoTokenizer.from_pretrained(str(model_directory), **tokenizer_kwargs)

    def create_model(use_gpu: bool):
        device = torch.device("cuda:0" if use_gpu else "cpu")
        dtype = torch.float16 if use_gpu else torch.float32
        model_kwargs = {"local_files_only": True}
        if use_gpu:
            model_kwargs["torch_dtype"] = dtype
        created = AutoModelForSeq2SeqLM.from_pretrained(str(model_directory), **model_kwargs)
        created.to(device)
        created.eval()
        return created, device, dtype

    gpu = gpu_is_available(torch)
    try:
        model, device, dtype = create_model(gpu)
    except (RuntimeError, OSError) as exc:
        if not gpu:
            raise
        # Auto mode must remain usable even when a Windows AMD runtime detects
        # the adapter but cannot initialize this particular model/kernel.
        clear_gpu_cache(torch)
        gpu = False
        try:
            model, device, dtype = create_model(False)
        except Exception:
            raise exc

    target_language_id = None
    if family == "nllb":
        target_language_id = tokenizer.convert_tokens_to_ids(TARGET_LANGUAGE)
        if target_language_id is None or target_language_id == tokenizer.unk_token_id:
            raise RuntimeError("NLLB tokenizer does not contain the Polish language token.")

    batch_size = default_batch_size(family, model_weight_size(model_directory), gpu)

    def run_generation(source_batch: Sequence[str], *, retry: bool) -> list[str]:
        if retry and len(source_batch) != 1:
            raise ValueError("degenerate-output retry requires exactly one segment")
        prepared = prepare_source_texts(family, source_batch)
        encoded = None
        generated = None
        try:
            encoded = tokenizer(
                prepared,
                return_tensors="pt",
                padding=True,
                truncation=True,
                max_length=512,
            )
            encoded = {key: value.to(device) for key, value in encoded.items()}
            kwargs = generation_kwargs(
                target_language_id,
                source_batch[0] if source_batch else "",
                retry=retry,
            )
            with torch.inference_mode():
                generated = model.generate(**encoded, **kwargs)
            return tokenizer.batch_decode(generated, skip_special_tokens=True)
        finally:
            encoded = None
            generated = None

    def translate_texts(texts: Sequence[str]) -> list[str]:
        nonlocal batch_size
        global _batch_size

        # Batches are padded to their longest member, so mixing a 3-word cue with
        # a 40-word one makes the short one cost as much as the long one. Grouping
        # similar lengths removes that waste; the original order is restored below,
        # and padding is masked, so the output is unchanged.
        order = length_batching_order(texts)
        ordered_texts = [texts[index] for index in order]

        output: list[str] = []
        start = 0
        while start < len(ordered_texts):
            current_size = min(batch_size, len(ordered_texts) - start)
            batch = list(ordered_texts[start : start + current_size])
            try:
                decoded = run_generation(batch, retry=False)

                if family == "madlad":
                    for index, (source_text, translated_text) in enumerate(
                        zip(batch, decoded, strict=True)
                    ):
                        reason = detect_degenerate_output(source_text, translated_text)
                        if reason is None:
                            continue

                        emit_diagnostic(
                            {
                                "event": "translation_degenerate_output",
                                "family": family,
                                "reason": reason,
                                "attempt": 1,
                                "action": "retry_single_segment",
                                "sourceWordCount": len(_word_tokens(source_text)),
                                "outputWordCount": len(_word_tokens(translated_text)),
                            }
                        )

                        retried_text = run_generation([source_text], retry=True)[0]
                        retry_reason = detect_degenerate_output(source_text, retried_text)
                        emit_diagnostic(
                            {
                                "event": "translation_degenerate_output_retry",
                                "family": family,
                                "reason": retry_reason or "none",
                                "attempt": 2,
                                "result": "still_degenerate" if retry_reason else "recovered",
                                "sourceWordCount": len(_word_tokens(source_text)),
                                "outputWordCount": len(_word_tokens(retried_text)),
                            }
                        )
                        decoded[index] = retried_text

                output.extend(decoded)
                start += current_size
            except RuntimeError as exc:
                message = str(exc).lower()
                if gpu and "out of memory" in message and current_size > 1:
                    batch_size = max(1, current_size // 2)
                    _batch_size = batch_size
                    clear_gpu_cache(torch)
                    continue
                raise

        return restore_original_order(output, order)

    _translate_texts = translate_texts
    _model_name = model_directory.name
    _batch_size = batch_size
    if gpu:
        try:
            _device_name = torch.cuda.get_device_name(0) or "GPU"
        except Exception:
            _device_name = "GPU"
        _dtype_name = "float16"
    else:
        _device_name = "CPU"
        _dtype_name = "float32"
    try:
        _runtime_version = f"transformers-{importlib.metadata.version('transformers')}"
    except importlib.metadata.PackageNotFoundError:
        _runtime_version = DEFAULT_RUNTIME_VERSION


def translate_segments(segments: list[dict]) -> list[tuple[int, str]]:
    if _translate_texts is None:
        raise RuntimeError("Offline MT model is not loaded.")
    if not isinstance(segments, list) or not segments:
        raise ValueError("translate requires at least one segment")

    ids: list[int] = []
    texts: list[str] = []
    seen_ids: set[int] = set()
    for segment in segments:
        if not isinstance(segment, dict):
            raise ValueError("invalid translation segment")
        segment_id = segment.get("id")
        text = segment.get("text")
        if (
            not isinstance(segment_id, int)
            or segment_id <= 0
            or segment_id in seen_ids
            or not isinstance(text, str)
        ):
            raise ValueError("invalid translation segment")
        seen_ids.add(segment_id)
        ids.append(segment_id)
        texts.append(text)

    translated = _translate_texts(texts)
    if len(translated) != len(texts) or any(not isinstance(text, str) for text in translated):
        raise RuntimeError("Offline MT runtime returned an invalid translation batch.")

    return list(zip(ids, translated, strict=True))


def translate_job(command: dict) -> None:
    job_id = command.get("jobId")
    segments = command.get("segments")
    if not isinstance(job_id, str) or not job_id.strip():
        raise ValueError("translate requires jobId")
    if not isinstance(segments, list) or not segments:
        raise ValueError("translate requires at least one segment")

    translated = translate_segments(segments)
    total = len(translated)
    for index, (segment_id, text) in enumerate(translated, start=1):
        emit({"type": "segment", "jobId": job_id, "id": segment_id, "text": text})
        emit({
            "type": "progress",
            "jobId": job_id,
            "completed": index,
            "total": total,
        })
    emit({"type": "complete", "jobId": job_id})


def safe_error(command_type: object, exc: Exception) -> tuple[str, str]:
    if command_type == "load":
        if isinstance(exc, RuntimeDependencyError):
            return "runtime_missing", "Offline MT Python runtime dependencies are not installed."
        return "model_load_failed", "Offline MT model could not be loaded."
    if command_type == "translate":
        if isinstance(exc, ValueError):
            return "invalid_command", str(exc)
        return "translation_failed", "Offline MT translation failed."
    return "invalid_command", "Unknown offline MT helper command."


def main() -> int:
    configure_stdio()
    for raw_line in sys.stdin:
        raw_line = raw_line.strip()
        if not raw_line:
            continue

        job_id = None
        command_type = None
        try:
            command = json.loads(raw_line)
            if not isinstance(command, dict):
                raise ValueError("command must be an object")
            command_type = command.get("type")
            job_id = command.get("jobId") if isinstance(command.get("jobId"), str) else None

            if command_type == "load":
                load_model(command.get("modelPath"))
                emit({
                    "type": "ready",
                    "model": _model_name,
                    "version": _runtime_version,
                    "device": _device_name,
                    "dtype": _dtype_name,
                    "batchSize": _batch_size,
                })
            elif command_type == "translate":
                translate_job(command)
            elif command_type == "shutdown":
                return 0
            else:
                raise ValueError("unknown command")
        except (json.JSONDecodeError, ValueError, RuntimeError, OSError) as exc:
            code, message = safe_error(command_type, exc)
            emit({"type": "error", "jobId": job_id, "code": code, "message": message})

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
