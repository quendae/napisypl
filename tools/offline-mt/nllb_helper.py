from __future__ import annotations

import gc
import importlib.metadata
import json
import pathlib
import sys
from collections.abc import Callable, Sequence

SOURCE_LANGUAGE = "eng_Latn"
TARGET_LANGUAGE = "pol_Latn"
MADLAD_TARGET_PREFIX = "<2pl> "
DEFAULT_RUNTIME_VERSION = "transformers-4.57.6"

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


def default_batch_size(family: str, model_size_bytes: int, gpu: bool) -> int:
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

    def translate_texts(texts: Sequence[str]) -> list[str]:
        nonlocal batch_size
        global _batch_size
        output: list[str] = []
        start = 0
        while start < len(texts):
            current_size = min(batch_size, len(texts) - start)
            batch = list(texts[start : start + current_size])
            prepared = prepare_source_texts(family, batch)
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
                generation_kwargs = {
                    "max_new_tokens": 256,
                    "num_beams": 4,
                }
                if target_language_id is not None:
                    generation_kwargs["forced_bos_token_id"] = target_language_id
                with torch.inference_mode():
                    generated = model.generate(**encoded, **generation_kwargs)
                output.extend(tokenizer.batch_decode(generated, skip_special_tokens=True))
                start += current_size
            except RuntimeError as exc:
                message = str(exc).lower()
                if gpu and "out of memory" in message and current_size > 1:
                    encoded = None
                    generated = None
                    batch_size = max(1, current_size // 2)
                    _batch_size = batch_size
                    clear_gpu_cache(torch)
                    continue
                raise
            finally:
                encoded = None
                generated = None
        return output

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
