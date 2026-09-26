from copy import deepcopy
from io import StringIO
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

from PIL import Image

from common import digest, file_hash, output_directory, relative_file
from contact_sheet import generate
from fetch import fetch, materialize
from raw_preview import embedded_preview
from verify_manifest import main, validate_rig, verify
from tests.fixtures import full_inputs


class PipelineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(dir=Path(__file__).parent)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name).resolve()
        self.sources = self.root / "sources"
        self.selection, self.edges, self.rig, self.licences = full_inputs(self.sources)

    def build(self, name="first"):
        with patch("fetch.urlopen", side_effect=AssertionError("Tests must remain offline")):
            return fetch(self.selection, self.edges, self.rig, self.licences,
                         self.root / name, self.sources, self.sources)

    def test_independent_runs_have_identical_manifest_hash_and_pass_g2(self):
        first = self.build()
        # Reorder picks and object keys; canonical output must still reproduce.
        self.edges.reverse()
        second = self.build("second")
        self.assertEqual(digest(first), digest(second))
        self.assertEqual(80, len(first["samples"]))
        with patch("sys.argv", ["verify_manifest.py", "--manifest",
                                str(self.root / "first/manifest.json"), "--compare",
                                str(self.root / "second/manifest.json")]), patch("sys.stdout", new=StringIO()) as out:
            main()
            self.assertIn("G2 PASS", out.getvalue())
        self.assertNotIn(str(self.root), (self.root / "first/manifest.json").read_text())

    def test_contact_sheet_has_all_overlays_raw_previews_and_escaped_attribution(self):
        self.edges[0]["author"] = "<script>alert(1)</script>"
        manifest = self.build()
        sheet = generate(manifest, self.root / "first", self.root / "sheet")
        html = sheet.read_text(encoding="utf-8")
        self.assertEqual(80, html.count("<figure>"))
        self.assertIn("&lt;script&gt;", html)
        self.assertNotIn("<script>", html)
        self.assertEqual(44, html.count("Ground-truth overlay"))
        self.assertEqual(6, html.count("Embedded RAW preview"))
        self.assertIn(digest(manifest), html)
        sample = next(s for s in manifest["samples"] if s["category"] == "subject-person")
        with Image.open(self.root / "sheet/thumbnails" / (sample["id"] + ".png")) as image:
            self.assertLessEqual(image.width, 480)
            self.assertGreater(image.getpixel((0, 0))[1], image.getpixel((0, 0))[0])

    def test_missing_attribution_and_bad_subquotas_fail_before_download(self):
        self.selection["samples"][0]["author"] = ""
        with self.assertRaisesRegex(ValueError, "author attribution"):
            self.build()
        self.selection["samples"][0]["author"] = "Reviewed creator"
        for sample in self.edges:
            sample["tags"] = []
        with self.assertRaisesRegex(ValueError, "backlit-or-flyaway"):
            self.build()

    def test_integrity_failure_and_existing_manifest_are_not_overwritten(self):
        manifest = self.build()
        path = relative_file(self.root / "first", manifest["samples"][0]["file"])
        path.write_bytes(b"corruption")
        with self.assertRaisesRegex(ValueError, "source hash mismatch"):
            verify(manifest, self.root / "first")
        with self.assertRaisesRegex(ValueError, "already exists"):
            self.build()

    def test_hash_pins_dimensions_and_missing_raw_preview_fail(self):
        edge = deepcopy(self.edges[-1])
        edge["sha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "expected SHA-256"):
            materialize(edge, self.root / "pin", self.sources)
        edge = deepcopy(self.edges[-1])
        edge["width"] = 2401
        with self.assertRaisesRegex(ValueError, "dimensions"):
            materialize(edge, self.root / "dimensions", self.sources)
        path = self.root / "no-preview.nef"
        path.write_bytes(b"no jpeg here")
        with self.assertRaisesRegex(ValueError, "No decodable"):
            embedded_preview(path)

    def test_duplicate_image_bytes_and_disallowed_licences_fail(self):
        self.edges[-1]["local_path"] = self.edges[-2]["local_path"]
        self.edges[-1]["sha256"] = self.edges[-2]["sha256"]
        with self.assertRaisesRegex(ValueError, "Duplicate image bytes"):
            self.build()
        self.edges[-1]["licence"] = "CC-BY-NC-4.0"
        with self.assertRaisesRegex(ValueError, "Disallowed"):
            self.build("disallowed")

    def test_licence_review_and_rig_identity_are_required(self):
        self.licences[0]["reviewed"] = False
        with self.assertRaisesRegex(ValueError, "host review"):
            self.build()
        altered = deepcopy(self.rig)
        altered["macos-15"]["cores"] = 4
        with self.assertRaisesRegex(ValueError, "architecture/core"):
            validate_rig(altered)

    def test_paths_cannot_escape_or_write_sample_data_inside_repo(self):
        with self.assertRaises(ValueError):
            relative_file(self.root, "../outside.jpg")
        with self.assertRaises(ValueError):
            output_directory(Path(__file__).parent / "forbidden-output")

    def test_negative_and_mask_dimensions_are_verified(self):
        manifest = self.build()
        sample = next(s for s in manifest["samples"] if s["category"] == "sky-negative")
        path = relative_file(self.root / "first", sample["mask_file"])
        Image.new("L", (sample["width"], sample["height"]), 255).save(path)
        sample["mask_sha256"] = file_hash(path)
        with self.assertRaisesRegex(ValueError, "negative contains sky"):
            verify(manifest, self.root / "first")


if __name__ == "__main__":
    unittest.main()
