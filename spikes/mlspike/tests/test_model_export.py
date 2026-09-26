"""Offline conversion plumbing tests; no model packages or downloads needed."""

import importlib.util
from pathlib import Path
import tempfile
import unittest

MODULE_PATH = Path(__file__).resolve().parents[1] / "models" / "export_common.py"
SPEC = importlib.util.spec_from_file_location("mlspike_export_common", MODULE_PATH)
EXPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORT)


class ModelExportTests(unittest.TestCase):
    def test_weights_hash_is_checked_before_loading(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "weights"
            path.write_bytes(b"abc")
            expected = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"
            self.assertEqual(expected, EXPORT.verify_weights(path, expected))
            with self.assertRaises(ValueError):
                EXPORT.verify_weights(path, "0" * 64)

    def test_model_output_must_stay_outside_repository(self):
        with self.assertRaises(ValueError):
            EXPORT.new_output(MODULE_PATH.parent / "model.onnx")

    def test_existing_models_and_configs_are_not_overwritten(self):
        with tempfile.TemporaryDirectory() as root:
            output = Path(root) / "model.onnx"
            output.write_bytes(b"source")
            with self.assertRaises(FileExistsError):
                EXPORT.new_output(output)
            self.assertEqual(b"source", output.read_bytes())
            output.unlink()
            output.with_suffix(".json").write_text("{}")
            with self.assertRaises(FileExistsError):
                EXPORT.new_output(output)

    def test_evidence_identifies_exact_output_and_keeps_probability_contract(self):
        import argparse
        import json
        with tempfile.TemporaryDirectory() as root:
            output = Path(root) / "model.onnx"
            output.write_bytes(b"converted")
            EXPORT.evidence(output, argparse.Namespace(candidate="u2netp"),
                            EXPORT.config("u2netp", 320))
            settings = json.loads(output.with_suffix(".json").read_text())
            self.assertEqual(EXPORT.file_hash(output), settings["model_sha256"])
            self.assertEqual("probability", settings["activation"])
            record = json.loads(output.with_suffix(".evidence.json").read_text())
            self.assertEqual("none", record["calibration"])
            self.assertEqual(9, record["output_bytes"])


if __name__ == "__main__":
    unittest.main()
