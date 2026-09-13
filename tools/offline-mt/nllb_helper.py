from __future__ import annotations

import importlib.metadata
import json
import pathlib
import sys
from collections.abc import Callable, Sequence

SOURCE_LANGUAGE = "eng_Latn"
TARGET_LANGUAGE = "pol_Latn"
DEFAULT_RUNTIME_VERSION = "transformers-4.57.6"

_translate_texts: Callable[[Sequence[str]], list[str]] | None = None
_model_name = "unknown"
_runtime_version = DEFAULT_RUNTIME_VERSION


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


def load_model(model_path: str) -> None:
    global _translate_texts, _model_name, _runtime_version

    model_directory = pathlib.Path(model_path) if model_path else None
    if model_directory is None or not model_directory.is_dir():
        raise FileNotFoundError("NLLB model directory was not found.")

    try:
        import torch
        from transformers import AutoModelForSeq2SeqLM, AutoTokenizer
    except ImportError as exc:
        raise RuntimeDependencyError(
            "NLLB Python runtime dependencies are not installed."
        ) from exc

    tokenizer = AutoTokenizer.from_pretrained(
        str(model_directory),
        src_lang=SOURCE_LANGUAGE,
        local_files_only=True,
    )
    model = AutoModelForSeq2SeqLM.from_pretrained(
        str(model_directory),
        local_files_only=True,
    )
    model.to("cpu")
    model.eval()

    target_language_id = tokenizer.convert_tokens_to_ids(TARGET_LANGUAGE)
    if target_language_id is None or target_language_id == tokenizer.unk_token_id:
        raise RuntimeError("NLLB tokenizer does not contain the Polish language token.")

    def translate_texts(texts: Sequence[str]) -> list[str]:
        output: list[str] = []
        batch_size = 8
        for start in range(0, len(texts), batch_size):
            batch = list(texts[start : start + batch_size])
            encoded = tokenizer(
                batch,
                return_tensors="pt",
                padding=True,
                truncation=True,
                max_length=512,
            )
            with torch.inference_mode():
                generated = model.generate(
                    **encoded,
                    forced_bos_token_id=target_language_id,
                    max_new_tokens=256,
                    num_beams=4,
                )
            output.extend(
                tokenizer.batch_decode(generated, skip_special_tokens=True)
            )
        return output

    _translate_texts = translate_texts
    _model_name = model_directory.name
    try:
        _runtime_version = f"transformers-{importlib.metadata.version('transformers')}"
    except importlib.metadata.PackageNotFoundError:
        _runtime_version = DEFAULT_RUNTIME_VERSION


def translate_segments(segments: list[dict]) -> list[tuple[int, str]]:
    if _translate_texts is None:
        raise RuntimeError("NLLB model is not loaded.")
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
        raise RuntimeError("NLLB runtime returned an invalid translation batch.")

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
            return "runtime_missing", "NLLB Python runtime dependencies are not installed."
        return "model_load_failed", "NLLB model could not be loaded."
    if command_type == "translate":
        if isinstance(exc, ValueError):
            return "invalid_command", str(exc)
        return "translation_failed", "NLLB translation failed."
    return "invalid_command", "Unknown NLLB helper command."


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
                emit({"type": "ready", "model": _model_name, "version": _runtime_version})
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
