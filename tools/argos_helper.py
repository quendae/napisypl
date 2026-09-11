from __future__ import annotations

import hashlib
import importlib.metadata
import json
import os
import shutil
import sys
import traceback
import zipfile
from pathlib import Path

import ctranslate2
import sentencepiece as spm

_translator = None
_tokenizer = None
_target_prefix = ""
_model_name = "unknown"


def emit(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def package_cache_dir(model_path: Path) -> Path:
    root = Path(os.environ.get("ARGOS_PACKAGES_DIR") or model_path.parent / "packages")
    root.mkdir(parents=True, exist_ok=True)
    fingerprint = hashlib.sha256(
        f"{model_path.resolve()}|{model_path.stat().st_size}|{model_path.stat().st_mtime_ns}".encode("utf-8")
    ).hexdigest()[:16]
    return root / f"{model_path.stem}-{fingerprint}"


def find_package_root(extracted: Path) -> Path:
    metadata_files = list(extracted.rglob("metadata.json"))
    if not metadata_files:
        raise FileNotFoundError("Argos package does not contain metadata.json")

    candidates = []
    for metadata_path in metadata_files:
        try:
            metadata = json.loads(metadata_path.read_text(encoding="utf-8"))
        except Exception:
            continue
        if metadata.get("from_code") == "en" and metadata.get("to_code") == "pl":
            candidates.append((metadata_path.parent, metadata))

    if not candidates:
        raise RuntimeError("Argos package does not contain an English to Polish translation model")

    package_root, metadata = candidates[0]
    model_dir = package_root / "model"
    tokenizer_path = package_root / "sentencepiece.model"
    if not model_dir.is_dir():
        raise FileNotFoundError("Argos package does not contain the CTranslate2 model directory")
    if not tokenizer_path.is_file():
        raise FileNotFoundError("Argos package does not contain sentencepiece.model")
    return package_root


def ensure_extracted(model_path: Path) -> tuple[Path, dict]:
    destination = package_cache_dir(model_path)
    ready_marker = destination / ".subflow-ready"

    if not ready_marker.is_file():
        temp_destination = destination.with_name(destination.name + ".partial")
        shutil.rmtree(temp_destination, ignore_errors=True)
        temp_destination.mkdir(parents=True, exist_ok=True)
        try:
            with zipfile.ZipFile(model_path, "r") as archive:
                archive.extractall(temp_destination)
            package_root = find_package_root(temp_destination)
            relative_root = package_root.relative_to(temp_destination)
            if destination.exists():
                shutil.rmtree(destination, ignore_errors=True)
            temp_destination.replace(destination)
            (destination / ".subflow-package-root").write_text(str(relative_root), encoding="utf-8")
            ready_marker.write_text("1", encoding="ascii")
        except Exception:
            shutil.rmtree(temp_destination, ignore_errors=True)
            raise

    relative_root_path = destination / ".subflow-package-root"
    if relative_root_path.is_file():
        relative_root = relative_root_path.read_text(encoding="utf-8").strip()
        package_root = destination / relative_root if relative_root else destination
    else:
        package_root = find_package_root(destination)

    metadata = json.loads((package_root / "metadata.json").read_text(encoding="utf-8"))
    return package_root, metadata


def load_model(model_path):
    global _translator, _tokenizer, _target_prefix, _model_name

    model_file = Path(model_path) if model_path else None
    if model_file is None or not model_file.is_file():
        raise FileNotFoundError(f"Argos model not found: {model_path}")

    package_root, metadata = ensure_extracted(model_file)
    _translator = ctranslate2.Translator(
        str(package_root / "model"),
        device="cpu",
        inter_threads=1,
        intra_threads=0,
        compute_type="auto",
    )
    _tokenizer = spm.SentencePieceProcessor(model_file=str(package_root / "sentencepiece.model"))
    _target_prefix = metadata.get("target_prefix", "") or ""
    _model_name = model_file.name

    try:
        version = f"ctranslate2-{importlib.metadata.version('ctranslate2')}"
    except Exception:
        version = "ctranslate2"
    emit({"type": "ready", "model": _model_name, "version": version})


def decode_result(result) -> str:
    pieces = result.hypotheses[0]
    value = _tokenizer.decode_pieces(pieces).replace("▁", " ").replace("_", " ")
    if _target_prefix and value.startswith(_target_prefix):
        value = value[len(_target_prefix):]
    if value.startswith(" "):
        value = value[1:]
    return value


def translate_segments(segments):
    flat_texts = []
    layouts = []

    for segment in segments:
        segment_id = segment.get("id")
        text = segment.get("text")
        if not isinstance(segment_id, int) or segment_id <= 0 or not isinstance(text, str):
            raise ValueError("invalid translation segment")

        normalized = text.replace("\r\n", "\n").replace("\r", "\n")
        line_indexes = []
        for line in normalized.split("\n"):
            if line == "":
                line_indexes.append(None)
            else:
                line_indexes.append(len(flat_texts))
                flat_texts.append(line)
        layouts.append((segment_id, line_indexes))

    translated_lines = []
    if flat_texts:
        tokenized = [_tokenizer.encode(text, out_type=str) for text in flat_texts]
        target_prefix = [[_target_prefix]] * len(tokenized) if _target_prefix else None
        results = _translator.translate_batch(
            tokenized,
            target_prefix=target_prefix,
            replace_unknowns=True,
            max_batch_size=32,
            batch_type="tokens",
            beam_size=4,
            num_hypotheses=1,
            length_penalty=0.2,
            return_scores=False,
        )
        translated_lines = [decode_result(result) for result in results]

    output = []
    for segment_id, line_indexes in layouts:
        lines = ["" if index is None else translated_lines[index] for index in line_indexes]
        output.append((segment_id, "\n".join(lines)))
    return output


def translate_job(command):
    if _translator is None or _tokenizer is None:
        raise RuntimeError("Argos model is not loaded")

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
        emit({"type": "progress", "jobId": job_id, "completed": index, "total": total})

    emit({"type": "complete", "jobId": job_id})


def main():
    # stdout is reserved for the newline-delimited JSON protocol.
    for raw_line in sys.stdin:
        raw_line = raw_line.strip()
        if not raw_line:
            continue
        job_id = None
        try:
            command = json.loads(raw_line)
            command_type = command.get("type")
            job_id = command.get("jobId")
            if command_type == "load":
                load_model(command.get("modelPath"))
            elif command_type == "translate":
                translate_job(command)
            elif command_type == "shutdown":
                return 0
            else:
                raise ValueError(f"unknown command type: {command_type}")
        except Exception as exc:
            print(traceback.format_exc(), file=sys.stderr, flush=True)
            emit({
                "type": "error",
                "jobId": job_id,
                "code": type(exc).__name__,
                "message": str(exc),
            })
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
