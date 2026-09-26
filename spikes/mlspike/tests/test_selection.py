from collections import Counter
from copy import deepcopy
import unittest

from common import digest
from select_labeled import select, sky_candidates, subject_candidates
from tests.fixtures import annotations, polygon


class SelectionTests(unittest.TestCase):
    def test_seed_counts_and_input_order_independence(self):
        instances, stuff = annotations()
        first = select(instances, stuff)
        self.assertEqual({"subject-person": 16, "subject-animal": 8,
                          "sky": 16, "sky-negative": 4},
                         Counter(s["category"] for s in first["samples"]))
        self.assertGreaterEqual(sum("tree-next-to-sky" in s["tags"]
                                    for s in first["samples"]), 6)
        instances["images"].reverse()
        instances["annotations"].reverse()
        stuff["annotations"].reverse()
        self.assertEqual(digest(first), digest(select(instances, stuff)))
        self.assertEqual(44, len({s["source_id"] for s in first["samples"]}))

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
            select(instances, stuff)


if __name__ == "__main__":
    unittest.main()
