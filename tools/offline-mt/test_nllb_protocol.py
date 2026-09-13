import importlib.util
import json
import pathlib
import subprocess
import sys
import unittest


class NllbProtocolTests(unittest.TestCase):
    def helper_path(self) -> pathlib.Path:
        return pathlib.Path(__file__).parent / "nllb_helper.py"

    def load_helper_module(self):
        helper = self.helper_path()
        self.assertTrue(helper.is_file(), f"NLLB helper source missing: {helper}")
        spec = importlib.util.spec_from_file_location("subflow_nllb_helper_test", helper)
        assert spec is not None and spec.loader is not None
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_invalid_command_is_structured_and_does_not_leak_payload(self):
        helper = self.helper_path()
        self.assertTrue(helper.is_file(), f"NLLB helper source missing: {helper}")

        process = subprocess.Popen(
            [sys.executable, str(helper)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
        )
        assert process.stdin is not None
        assert process.stdout is not None
        assert process.stderr is not None

        sentinel = "LEAK_SENTINEL_nllb_protocol"
        process.stdin.write(json.dumps({"type": "unknown", "payload": sentinel}) + "\n")
        process.stdin.flush()

        response = json.loads(process.stdout.readline())
        self.assertEqual("error", response["type"])
        self.assertEqual("invalid_command", response["code"])
        self.assertIsNone(response.get("jobId"))

        process.stdin.write(json.dumps({"type": "shutdown"}) + "\n")
        process.stdin.flush()
        process.stdin.close()

        stderr = process.stderr.read()
        exit_code = process.wait(timeout=10)
        self.assertEqual(0, exit_code)
        self.assertNotIn(sentinel, stderr)

    def test_translate_segments_preserves_ids_and_order_without_heavy_runtime(self):
        module = self.load_helper_module()
        module._translate_texts = lambda texts: [f"PL:{text}" for text in texts]

        translated = module.translate_segments(
            [
                {"id": 7, "text": "Hello"},
                {"id": 11, "text": "Goodbye"},
            ]
        )

        self.assertEqual([(7, "PL:Hello"), (11, "PL:Goodbye")], translated)

    def test_translate_segments_rejects_invalid_segment_without_echoing_text(self):
        module = self.load_helper_module()
        module._translate_texts = lambda texts: list(texts)
        sentinel = "LEAK_SENTINEL_invalid_nllb_segment"

        with self.assertRaisesRegex(ValueError, "invalid translation segment") as context:
            module.translate_segments([{"id": 0, "text": sentinel}])

        self.assertNotIn(sentinel, str(context.exception))

    def test_infer_model_family_recognizes_nllb_and_madlad(self):
        module = self.load_helper_module()

        self.assertEqual("nllb", module.infer_model_family({"model_type": "m2m_100"}))
        self.assertEqual("madlad", module.infer_model_family({"model_type": "t5"}))
        with self.assertRaisesRegex(RuntimeError, "unsupported"):
            module.infer_model_family({"model_type": "unknown"})

    def test_prepare_source_texts_adds_polish_prefix_only_for_madlad(self):
        module = self.load_helper_module()

        self.assertEqual(["Hello", "Goodbye"], module.prepare_source_texts("nllb", ["Hello", "Goodbye"]))
        self.assertEqual(["<2pl> Hello", "<2pl> Goodbye"], module.prepare_source_texts("madlad", ["Hello", "Goodbye"]))

    def test_default_batch_size_depends_on_family_size_and_device(self):
        module = self.load_helper_module()

        self.assertEqual(32, module.default_batch_size("nllb", 2_500_000_000, gpu=True))
        self.assertEqual(16, module.default_batch_size("nllb", 5_500_000_000, gpu=True))
        self.assertEqual(8, module.default_batch_size("madlad", 11_800_000_000, gpu=True))
        self.assertEqual(8, module.default_batch_size("nllb", 2_500_000_000, gpu=False))
        self.assertEqual(4, module.default_batch_size("nllb", 5_500_000_000, gpu=False))
        self.assertEqual(2, module.default_batch_size("madlad", 11_800_000_000, gpu=False))


if __name__ == "__main__":
    unittest.main()
