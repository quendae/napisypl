import json
import os
import pathlib
import sys
from typing import Callable, Optional


class TranslatorRuntime:
    def __init__(self, loader: Optional[Callable[[str], tuple[object, str]]] = None):
        self._loader = loader or _load_argos_translation
        self._translation = None
        self._model_version = None

    def handle(self, command, emit: Callable[[dict], None]) -> bool:
        if not isinstance(command, dict):
            _emit_error(emit, None, "invalid_request", "Command must be a JSON object.")
            return True

        command_type = command.get("type")
        if not isinstance(command_type, str) or not command_type:
            _emit_error(emit, _safe_job_id(command), "invalid_request", "Command type is required.")
            return True

        if command_type == "load":
            return self._handle_load(command, emit)
        if command_type == "translate":
            return self._handle_translate(command, emit)
        if command_type == "shutdown":
            return False

        _emit_error(emit, _safe_job_id(command), "unknown_command", "Unknown command type.")
        return True

    def _handle_load(self, command, emit: Callable[[dict], None]) -> bool:
        model_path = command.get("modelPath")
        if not isinstance(model_path, str) or not model_path.strip():
            _emit_error(emit, None, "invalid_request", "modelPath is required.")
            return True

        try:
            translation, version = self._loader(model_path)
        except Exception:
            _emit_error(emit, None, "model_load_failed", "Could not load the local EN-PL translation model.")
            return True

        self._translation = translation
        self._model_version = str(version)
        emit({"type": "ready", "model": "en-pl", "version": self._model_version})
        return True

    def _handle_translate(self, command, emit: Callable[[dict], None]) -> bool:
        job_id = _safe_job_id(command)
        if job_id is None:
            _emit_error(emit, None, "invalid_request", "jobId is required.")
            return True

        if self._translation is None:
            _emit_error(emit, job_id, "model_not_loaded", "Load a translation model first.")
            return True

        segments = command.get("segments")
        validated = _validate_segments(segments)
        if validated is None:
            _emit_error(emit, job_id, "invalid_request", "segments must contain unique positive ids and text strings.")
            return True

        total = len(validated)
        for completed, segment in enumerate(validated, start=1):
            try:
                translated = self._translation.translate(segment["text"])
            except Exception:
                _emit_error(emit, job_id, "translation_failed", "Local translation failed.")
                return True

            if not isinstance(translated, str):
                _emit_error(emit, job_id, "translation_failed", "Local translator returned an invalid result.")
                return True

            emit({
                "type": "segment",
                "jobId": job_id,
                "id": segment["id"],
                "text": translated,
            })
            emit({
                "type": "progress",
                "jobId": job_id,
                "completed": completed,
                "total": total,
            })

        emit({"type": "complete", "jobId": job_id})
        return True


def _safe_job_id(command) -> Optional[str]:
    if not isinstance(command, dict):
        return None
    value = command.get("jobId")
    return value if isinstance(value, str) and value.strip() else None


def _validate_segments(segments):
    if not isinstance(segments, list) or not segments:
        return None

    validated = []
    seen_ids = set()
    for segment in segments:
        if not isinstance(segment, dict):
            return None
        segment_id = segment.get("id")
        text = segment.get("text")
        if not isinstance(segment_id, int) or isinstance(segment_id, bool) or segment_id <= 0:
            return None
        if segment_id in seen_ids or not isinstance(text, str):
            return None
        seen_ids.add(segment_id)
        validated.append({"id": segment_id, "text": text})
    return validated


def _emit_error(emit: Callable[[dict], None], job_id: Optional[str], code: str, message: str):
    payload = {"type": "error", "code": code, "message": message}
    if job_id is not None:
        payload["jobId"] = job_id
    emit(payload)


def _load_argos_translation(model_path: str):
    # Imports are intentionally lazy: protocol/unit tests do not need the ML runtime.
    import argostranslate.package
    import argostranslate.translate

    path = pathlib.Path(model_path).expanduser().resolve()
    if not path.is_file():
        raise FileNotFoundError(path)

    installed_languages = argostranslate.translate.get_installed_languages()
    translation = _find_translation(installed_languages)
    if translation is None:
        argostranslate.package.install_from_path(path)
        installed_languages = argostranslate.translate.get_installed_languages()
        translation = _find_translation(installed_languages)

    if translation is None:
        raise RuntimeError("EN-PL translation package is not available after installation")

    version = _model_version_from_filename(path.name)
    return translation, version


def _find_translation(languages):
    source = next((language for language in languages if getattr(language, "code", None) == "en"), None)
    target = next((language for language in languages if getattr(language, "code", None) == "pl"), None)
    if source is None or target is None:
        return None
    try:
        return source.get_translation(target)
    except Exception:
        return None


def _model_version_from_filename(filename: str) -> str:
    stem = pathlib.Path(filename).stem
    marker = "translate-en_pl-"
    return stem[len(marker):] if stem.startswith(marker) else stem


def _write_event(payload: dict):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def run_loop(runtime: Optional[TranslatorRuntime] = None):
    runtime = runtime or TranslatorRuntime()
    for raw_line in sys.stdin:
        if not raw_line.strip():
            continue
        try:
            command = json.loads(raw_line)
        except json.JSONDecodeError:
            _emit_error(_write_event, None, "invalid_json", "Input line is not valid JSON.")
            continue

        if not runtime.handle(command, _write_event):
            break


def main():
    if hasattr(sys.stdin, "reconfigure"):
        sys.stdin.reconfigure(encoding="utf-8")
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    run_loop()


if __name__ == "__main__":
    main()
