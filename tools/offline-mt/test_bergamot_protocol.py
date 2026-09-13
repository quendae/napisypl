import json
import os
import pathlib
import subprocess
import unittest


class BergamotProtocolTests(unittest.TestCase):
    def helper_path(self) -> pathlib.Path:
        configured = os.environ.get("SUBFLOW_BERGAMOT_HELPER")
        if configured:
            return pathlib.Path(configured)
        return pathlib.Path(__file__).parent / "bergamot-helper" / "bin" / "SubFlow.BergamotHelper.exe"

    def test_invalid_command_is_structured_and_does_not_leak_payload(self):
        helper = self.helper_path()
        self.assertTrue(helper.is_file(), f"Bergamot helper artifact missing: {helper}")

        process = subprocess.Popen(
            [str(helper)],
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
        )
        assert process.stdin is not None
        assert process.stdout is not None
        assert process.stderr is not None

        sentinel = "LEAK_SENTINEL_bergamot_protocol"
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


if __name__ == "__main__":
    unittest.main()
