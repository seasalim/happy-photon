"""Select seed-51 COCO val2017 subject and COCO-Stuff sky samples."""

import argparse
from collections import defaultdict
import random
from pathlib import Path

from common import (ANIMALS, SEED, coco_licences, file_hash, output_directory,
                    read_json, sorted_samples, write_json)
from masks import adjacent, union

# Ceiling labels provide a conservative indoor cue; floor labels can be outdoors.
INDOOR = {"ceiling-other", "ceiling-tile"}


def indexed(document):
    groups = defaultdict(list)
    for item in document["annotations"]:
        groups[item["image_id"]].append(item)
    return groups


def base_sample(image, category, licence):
    url = image.get("coco_url")
    if not url:
        raise ValueError(f"COCO image {image['id']} has no source URL")
    return {
        "id": f"coco-{category}-{image['id']}", "source": "coco",
        "source_id": str(image["id"]), "url": url,
        "attribution_url": image.get("flickr_url", ""),
        "author": image.get("author", ""), "licence": licence,
        "category": category, "width": image["width"], "height": image["height"],
        "tags": [], "raw": False,
    }


def subject_candidates(document):
    licences = coco_licences(document)
    categories = {item["id"]: item["name"] for item in document["categories"]}
    groups = indexed(document)
    result = {"subject-person": [], "subject-animal": []}
    for image in sorted(document["images"], key=lambda item: item["id"]):
        if image["license"] not in licences or max(image["width"], image["height"]) < 600:
            continue
        annotations = groups[image["id"]]
        targets = [a for a in annotations if not a.get("iscrowd", 0) and
                   categories[a["category_id"]] in ANIMALS | {"person"}]
        if len(targets) != 1:
            continue
        target = targets[0]
        frame = image["width"] * image["height"]
        others = sum(a["area"] for a in annotations if a["id"] != target["id"])
        if target["area"] < 0.15 * frame or others > 0.05 * frame:
            continue
        category = ("subject-person" if categories[target["category_id"]] == "person"
                    else "subject-animal")
        sample = base_sample(image, category, licences[image["license"]])
        sample.update(mask_annotations=[target],
                      evidence={"subject_fraction": target["area"] / frame,
                                "other_fraction": others / frame,
                                "noncrowd_targets": len(targets)})
        result[category].append(sample)
    return result


def sky_candidates(instances, stuff):
    licences = coco_licences(instances)
    images = {item["id"]: item for item in instances["images"]}
    categories = {item["id"]: item["name"] for item in stuff["categories"]}
    if not {"sky-other", "clouds"} <= set(categories.values()):
        raise ValueError("COCO-Stuff must declare sky-other and clouds categories")
    groups = indexed(stuff)
    result = {"sky": [], "sky-negative": []}
    for image_id in sorted(groups):
        image = images.get(image_id)
        if image is None or image["license"] not in licences:
            continue
        annotations = groups[image_id]
        sky_annotations = [a for a in annotations
                           if categories[a["category_id"]] in {"sky-other", "clouds"}]
        width, height = image["width"], image["height"]
        sky = union(sky_annotations, width, height)
        fraction = float(sky.mean())
        indoor = [a for a in annotations if categories[a["category_id"]] in INDOOR]
        indoor_fraction = float(union(indoor, width, height).mean()) if indoor else 0.0
        if 0.10 <= fraction <= 0.70:
            category = "sky"
        elif fraction == 0 and indoor_fraction > 0:
            category = "sky-negative"
        else:
            continue
        trees = [a for a in annotations if categories[a["category_id"]] == "tree"]
        tree = union(trees, width, height)
        tree_fraction = float(tree.mean())
        next_to_sky = tree_fraction >= 0.05 and adjacent(sky, tree)
        sample = base_sample(image, category, licences[image["license"]])
        sample.update(mask_annotations=sorted(sky_annotations, key=lambda a: a["id"]),
                      evidence={"sky_fraction": fraction, "tree_fraction": tree_fraction,
                                "tree_adjacent": next_to_sky,
                                "indoor_fraction": indoor_fraction})
        if next_to_sky:
            sample["tags"].append("tree-next-to-sky")
        result[category].append(sample)
    return result


def choose(candidates, count, rng, label):
    ordered = sorted(candidates, key=lambda item: int(item["source_id"]))
    if len(ordered) < count:
        raise ValueError(f"Insufficient {label}: {len(ordered)} available, {count} required")
    rng.shuffle(ordered)
    return ordered[:count]


def select(instances, stuff, seed=SEED):
    rng = random.Random(seed)
    subject = subject_candidates(instances)
    sky = sky_candidates(instances, stuff)
    selected = choose(subject["subject-person"], 16, rng, "people")
    selected += choose(subject["subject-animal"], 8, rng, "animals")
    used = {s["source_id"] for s in selected}
    sky = {key: [s for s in rows if s["source_id"] not in used]
           for key, rows in sky.items()}
    trees = choose([s for s in sky["sky"] if "tree-next-to-sky" in s["tags"]],
                   6, rng, "sky with adjacent trees")
    tree_ids = {s["id"] for s in trees}
    selected += trees + choose([s for s in sky["sky"] if s["id"] not in tree_ids],
                               10, rng, "remaining sky")
    selected += choose(sky["sky-negative"], 4, rng, "indoor sky negatives")
    return {"schema_version": 1, "seed": seed, "samples": sorted_samples(selected)}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instances", type=Path, required=True)
    parser.add_argument("--stuff", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--seed", type=int, choices=[SEED], default=SEED)
    args = parser.parse_args()
    result = select(read_json(args.instances), read_json(args.stuff), args.seed)
    result["annotations"] = {
        "instances": file_hash(args.instances), "stuff": file_hash(args.stuff)}
    write_json(output_directory(args.output_dir) / "labeled.json", result)


if __name__ == "__main__":
    main()
