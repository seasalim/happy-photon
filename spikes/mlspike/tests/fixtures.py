"""Small generated public-data stand-ins; never use real sample images in tests."""

from pathlib import Path

from PIL import Image

from common import QUOTAS, SUBQUOTAS, digest, file_hash
from select_labeled import select


def polygon(left, top, right, bottom):
    return [[left, top, right, top, right, bottom, left, bottom]]


def annotations():
    instances = {
        "licenses": [{"id": 4, "name": "Attribution License",
                      "url": "http://creativecommons.org/licenses/by/2.0/"},
                     {"id": 1, "name": "Attribution-NonCommercial",
                      "url": "http://creativecommons.org/licenses/by-nc/2.0/"}],
        "categories": [{"id": 1, "name": "person"}, {"id": 2, "name": "dog"},
                       {"id": 3, "name": "chair"}],
        "images": [], "annotations": [],
    }
    stuff = {"categories": [
        {"id": 100, "name": "sky-other"}, {"id": 101, "name": "clouds"},
        {"id": 102, "name": "tree"}, {"id": 103, "name": "ceiling-tile"}],
        "annotations": []}
    for index in range(1, 57):
        instances["images"].append({
            "id": index, "width": 600, "height": 10, "license": 4,
            "coco_url": f"https://images.cocodataset.org/val2017/{index}.jpg",
            "flickr_url": f"https://example.test/creator/{index}", "author": "Test creator",
        })
        if index <= 28:
            instances["annotations"].append({
                "id": index, "image_id": index, "category_id": 1 if index <= 18 else 2,
                "iscrowd": 0, "area": 1200, "segmentation": polygon(0, 0, 120, 10)})
        elif index <= 50:
            stuff["annotations"].extend([
                {"id": index * 10, "image_id": index, "category_id": 100,
                 "segmentation": polygon(0, 0, 300, 10)},
                {"id": index * 10 + 1, "image_id": index, "category_id": 102,
                 "segmentation": polygon(300, 0, 360, 10)},
            ])
        else:
            stuff["annotations"].append({
                "id": index * 10, "image_id": index, "category_id": 103,
                "segmentation": polygon(0, 0, 600, 10)})
    return instances, stuff


def attribution():
    return {str(index): {"author": f"Test creator {index}",
                         "attribution_url": f"https://www.flickr.com/photos/test/{index}/"}
            for index in range(1, 57)}


def rig():
    return {
        "windows-2025": {"runner_image": "windows-2025-test", "cpu_model": "AMD EPYC 7763",
                         "architecture": "x64", "cores": 4, "ram_bytes": 16 * 1024 ** 3},
        "ubuntu-24.04": {"runner_image": "ubuntu-24.04-test", "cpu_model": "Xeon 6973P-C",
                        "architecture": "x64", "cores": 4, "ram_bytes": int(15.6 * 1024 ** 3)},
        "macos-15": {"runner_image": "macos-15-test", "cpu_model": "Apple M1 (Virtual)",
                     "architecture": "arm64", "cores": 3, "ram_bytes": 7 * 1024 ** 3},
    }


def full_inputs(root):
    root = Path(root)
    root.mkdir(parents=True, exist_ok=True)
    instances, stuff = annotations()
    selection = select(instances, stuff, attribution())
    selection["attribution_sha256"] = digest(attribution())
    selection["annotations"] = {"instances": digest(instances), "stuff": digest(stuff)}
    for sample in selection["samples"]:
        path = root / f"{sample['id']}.jpg"
        index = int(sample["source_id"])
        color = (index * 13 % 256, index * 41 % 256, index * 97 % 256)
        Image.new("RGB", (600, 10), color).save(path)
        sample["local_path"] = path.name
    edges = []
    for category, count in list(QUOTAS.items())[4:]:
        for index in range(count):
            raw = category == "portrait" and index < 6
            name = f"edge-{category}-{index}"
            suffix = ".nef" if raw else ".jpg"
            path = root / (name + suffix)
            if raw:
                preview_path = root / "embedded.jpg"
                Image.new("RGB", (48, 32), (index * 31, 80, 90)).save(preview_path)
                path.write_bytes(b"SYNTHETIC-RAW\0" + preview_path.read_bytes())
            else:
                Image.new("RGB", (2400, 4), (len(edges) * 7, 170, 40)).save(path)
            edges.append({
                "id": name, "source": "raw.pixls.us" if raw else "commons",
                "source_id": name,
                "url": ("https://raw.pixls.us/" if raw else
                        "https://upload.wikimedia.org/") + path.name,
                "author": "Synthetic author", "licence": "CC0-1.0", "category": category,
                "width": 2400, "height": 4, "raw": raw,
                "tags": [tag for tag, minimum in SUBQUOTAS.get(category, {}).items()
                         if index < minimum],
                "local_path": path.name, "sha256": file_hash(path),
                **({"dimensions_evidence": "Synthetic fixture metadata"} if raw else {}),
            })
    records = []
    for name in ("instances", "stuff"):
        path = root / f"{name}-licence.txt"
        path.write_text("Synthetic annotation licence evidence", encoding="utf-8")
        records.append({"name": name, "path": path.name, "sha256": file_hash(path),
                        "url": f"https://example.test/{name}/license",
                        "licence": "CC-BY-4.0", "reviewed": True})
    return selection, edges, rig(), records
