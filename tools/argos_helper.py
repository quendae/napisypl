import importlib.metadata
import json
import os
import sys
import traceback

import argostranslate.package
import argostranslate.translate

_translation = None
_model_name = "unknown"


def emit(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def find_translation():
    languages = argostranslate.translate.get_installed_languages()
    source = next((language for language in languages if language.code == "en"), None)
    target = next((language for language in languages if language.code == "pl"), None)
    if source is None or target is None:
        return None
    try:
        return source.get_translation(target)
    except Exception:
        return None


def load_model(model_path):
    global _translation, _model_name
    if not model_path or not os.path.isfile(model_path):
        raise FileNotFoundError(f"Argos model not found: {model_path}")

    _translation = find_translation()
    if _translation is None:
        argostranslate.package.install_from_path(model_path)
        _translation = find_translation()
    if _translation is None:
        raise RuntimeError("Argos English to Polish translation was not available after model installation")

    _model_name = os.path.basename(model_path)
    try:
        version = importlib.metadata.version("argostranslate")
    except Exception:
        version = "unknown"
    emit({"type": "ready", "model": _model_name, "version": version})


def translate_job(command):
    if _translation is None:
        raise RuntimeError("Argos model is not loaded")

    job_id = command.get("jobId")
    segments = command.get("segments")
    if not isinstance(job_id, str) or not job_id.strip():
        raise ValueError("translate requires jobId")
    if not isinstance(segments, list) or not segments:
        raise ValueError("translate requires at least one segment")

    total = len(segments)
    for index, segment in enumerate(segments, start=1):
        segment_id = segment.get("id")
        text = segment.get("text")
        if not isinstance(segment_id, int) or segment_id <= 0 or not isinstance(text, str):
            raise ValueError("invalid translation segment")
        translated = _translation.translate(text)
        emit({"type": "segment", "jobId": job_id, "id": segment_id, "text": translated})
        emit({"type": "progress", "jobId": job_id, "completed": index, "total": total})

    emit({"type": "complete", "jobId": job_id})


def main():
    # Parent process sets ARGOS_PACKAGES_DIR before launching this helper.
    # Keep stdout reserved for protocol JSON; diagnostics go to stderr.
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
