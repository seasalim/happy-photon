from collections import Counter
from copy import deepcopy
import unittest
import random
from io import StringIO
from pathlib import Path
import tempfile
from unittest.mock import patch

from common import digest, file_hash, read_json, write_json
from select_labeled import (NeedsAttribution, choose, main, select,
                            sky_candidates, subject_candidates)
from tests.fixtures import annotations, attribution, polygon


class SelectionTests(unittest.TestCase):
    def test_seed_counts_and_input_order_independence(self):
        instances, stuff = annotations()
        first = select(instances, stuff, attribution())
        self.assertEqual({"subject-person": 16, "subject-animal": 8,
                          "sky": 16, "sky-negative": 4},
                         Counter(s["category"] for s in first["samples"]))
        self.assertGreaterEqual(sum("tree-next-to-sky" in s["tags"]
                                    for s in first["samples"]), 6)
        instances["images"].reverse()
        instances["annotations"].reverse()
        stuff["annotations"].reverse()
        self.assertEqual(digest(first), digest(select(instances, stuff, attribution())))
        self.assertEqual(44, len({s["source_id"] for s in first["samples"]}))

    def test_unresolved_draws_are_skipped_without_reshuffling(self):
        instances, stuff = annotations()
        snapshot = attribution()
        people = subject_candidates(instances)["subject-person"]
        original = choose(people, 16, random.Random(51), "people", snapshot)
        excluded = original[0]["source_id"]
        snapshot[excluded] = {"unresolved": "Photo deleted"}
        replacement = choose(people, 16, random.Random(51), "people", snapshot)
        self.assertEqual([s["source_id"] for s in original[1:]],
                         [s["source_id"] for s in replacement[:15]])
        self.assertNotIn(excluded, {s["source_id"] for s in replacement})
        # Skip one animal, sky and negative too; quotas and tree reserve survive.
        for category in ("subject-animal", "sky", "sky-negative"):
            sample = next(s for s in select(instances, stuff, snapshot)["samples"]
                          if s["category"] == category)
            snapshot[sample["source_id"]] = {"unresolved": "No recoverable owner"}
        selected = select(instances, stuff, snapshot)
        self.assertEqual(44, len(selected["samples"]))
        self.assertGreaterEqual(sum("tree-next-to-sky" in s["tags"]
                                    for s in selected["samples"]), 6)
        instances["images"].reverse()
        self.assertEqual(digest(selected), digest(select(instances, stuff, snapshot)))

    def test_unknown_draw_requests_shortfall_and_margin_in_draw_order(self):
        candidates = [{"source_id": str(i), "attribution_url": f"https://test/{i}"}
                      for i in range(30)]
        ordered = candidates.copy()
        random.Random(51).shuffle(ordered)
        snapshot = {ordered[0]["source_id"]: {"author": "Known", "attribution_url": "url"},
                    ordered[1]["source_id"]: {"unresolved": "Deleted"}}
        with self.assertRaises(NeedsAttribution) as caught:
            choose(candidates, 10, random.Random(51), "test", snapshot)
        self.assertEqual([int(s["source_id"]) for s in ordered[2:16]],
                         [s["image_id"] for s in caught.exception.needs])
        snapshot = {s["source_id"]: {"unresolved": "Deleted"} for s in candidates}
        with self.assertRaisesRegex(ValueError, "0 attributable"):
            choose(candidates, 10, random.Random(51), "test", snapshot)

    def test_cli_pauses_without_selection_and_records_exact_snapshot_hash(self):
        with tempfile.TemporaryDirectory(dir=Path(__file__).parent) as temporary:
            root = Path(temporary).resolve()
            instances, stuff = annotations()
            for name, data in (("instances", instances), ("stuff", stuff), ("attribution", {})):
                write_json(root / f"{name}.json", data)
            argv = ["select_labeled.py", "--instances", str(root / "instances.json"),
                    "--stuff", str(root / "stuff.json"), "--attribution",
                    str(root / "attribution.json"), "--output-dir", str(root)]
            with patch("sys.argv", argv), patch("select_labeled.output_directory", return_value=root):
                with patch("sys.stderr", new=StringIO()), self.assertRaises(SystemExit) as caught:
                    main()
                self.assertEqual(2, caught.exception.code)
                self.assertFalse((root / "labeled.json").exists())
                needs = read_json(root / "needs-attribution.json")
                self.assertEqual(18, len(needs))
                self.assertTrue(all(set(item) == {"image_id", "flickr_url"} for item in needs))
                # Exact file hash, including whitespace, rather than a reserialized object hash.
                write_json(root / "attribution.json", attribution())
                with (root / "attribution.json").open("a") as stream:
                    stream.write("  ")
                main()
                first = read_json(root / "labeled.json")
                self.assertEqual(file_hash(root / "attribution.json"), first["attribution_sha256"])
                self.assertFalse((root / "needs-attribution.json").exists())
                (root / "labeled.json").unlink()
                main()
                self.assertEqual(first, read_json(root / "labeled.json"))

    def test_licence_size_crowd_and_coverage_boundaries(self):
        instances, _ = annotations()
        instances["images"][0]["license"] = 1
        instances["images"][1]["width"] = 599
        instances["annotations"][2]["iscrowd"] = 1
        instances["annotations"][3]["area"] = 899
        instances["annotations"][4]["area"] = 900
        instances["annotations"].extend([
            {"id": 1000, "image_id": 6, "category_id": 3, "area": 301, "iscrowd": 0},
            {"id": 1001, "image_id": 7, "category_id": 3, "area": 300, "iscrowd": 0},
        ])
        result = subject_candidates(instances)["subject-person"]
        ids = {s["source_id"] for s in result}
        self.assertTrue({"5", "7"} <= ids)
        self.assertFalse({"1", "2", "3", "4", "6"} & ids)

    def test_multiple_people_are_not_single_subject_candidates(self):
        instances, _ = annotations()
        extra = deepcopy(instances["annotations"][0])
        extra["id"] = 1000
        extra["area"] = 10
        instances["annotations"].append(extra)
        self.assertNotIn("1", {s["source_id"] for s in
                              subject_candidates(instances)["subject-person"]})

    def test_sky_bounds_indoor_and_tree_adjacency(self):
        instances, stuff = annotations()
        # Above upper bound; below lower bound; separated tree.
        stuff["annotations"][0]["segmentation"] = polygon(0, 0, 421, 10)
        stuff["annotations"][2]["segmentation"] = polygon(0, 0, 59, 10)
        stuff["annotations"][5]["segmentation"] = polygon(301, 0, 361, 10)
        stuff["annotations"].append({
            "id": 9999, "image_id": 51, "category_id": 101,
            "segmentation": polygon(0, 0, 1, 1)})
        result = sky_candidates(instances, stuff)
        sky = {s["source_id"]: s for s in result["sky"]}
        self.assertFalse({"29", "30"} & set(sky))
        self.assertNotIn("tree-next-to-sky", sky["31"]["tags"])
        self.assertNotIn("51", {s["source_id"] for s in result["sky-negative"]})

    def test_floor_only_scene_is_not_assumed_indoors(self):
        instances, stuff = annotations()
        stuff["categories"][-1]["name"] = "floor-stone"
        self.assertEqual([], sky_candidates(instances, stuff)["sky-negative"])

    def test_insufficient_quotas_fail_instead_of_relaxing_rules(self):
        instances, stuff = annotations()
        instances["images"] = instances["images"][10:]
        with self.assertRaisesRegex(ValueError, "Insufficient people"):
            select(instances, stuff, attribution())


if __name__ == "__main__":
    unittest.main()
