import json
import pathlib
import sys
import unittest

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from translator import TranslatorRuntime


class FakeTranslation:
    def translate(self, text):
        return "PL: " + text


class TranslatorRuntimeTests(unittest.TestCase):
    def setUp(self):
        self.events = []
        self.runtime = TranslatorRuntime(loader=lambda model_path: (FakeTranslation(), "test-1"))

    def emit(self, payload):
        self.events.append(payload)

    def test_load_translate_and_shutdown(self):
        keep_running = self.runtime.handle({"type": "load", "modelPath": "model.argosmodel"}, self.emit)
        self.assertTrue(keep_running)
        self.assertEqual("ready", self.events[-1]["type"])
        self.assertEqual("en-pl", self.events[-1]["model"])

        keep_running = self.runtime.handle(
            {
                "type": "translate",
                "jobId": "job-1",
                "segments": [
                    {"id": 7, "text": "Hello"},
                    {"id": 8, "text": "Bye"},
                ],
            },
            self.emit,
        )
        self.assertTrue(keep_running)
        self.assertEqual(
            ["segment", "progress", "segment", "progress", "complete"],
            [event["type"] for event in self.events[-5:]],
        )
        self.assertEqual("PL: Hello", self.events[-5]["text"])
        self.assertEqual(2, self.events[-2]["completed"])
        self.assertEqual(2, self.events[-2]["total"])

        self.assertFalse(self.runtime.handle({"type": "shutdown"}, self.emit))

    def test_translate_requires_loaded_model(self):
        self.runtime.handle(
            {"type": "translate", "jobId": "job-1", "segments": [{"id": 1, "text": "Hello"}]},
            self.emit,
        )
        self.assertEqual("error", self.events[-1]["type"])
        self.assertEqual("model_not_loaded", self.events[-1]["code"])
        self.assertEqual("job-1", self.events[-1]["jobId"])

    def test_invalid_request_emits_protocol_error_without_echoing_subtitle(self):
        private_text = "private subtitle that must not appear in errors"
        self.runtime.handle({"type": "translate", "jobId": "job-2", "segments": [{"text": private_text}]}, self.emit)
        event = self.events[-1]
        self.assertEqual("error", event["type"])
        self.assertEqual("invalid_request", event["code"])
        self.assertNotIn(private_text, json.dumps(event))

    def test_unknown_command_emits_error_and_process_can_continue(self):
        keep_running = self.runtime.handle({"type": "mystery"}, self.emit)
        self.assertTrue(keep_running)
        self.assertEqual("unknown_command", self.events[-1]["code"])


if __name__ == "__main__":
    unittest.main()
