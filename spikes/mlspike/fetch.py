"""Fetch the reviewed public pick list and emit an immutable canonical manifest."""

import argparse
from copy import deepcopy
from pathlib import Path
import shutil
import tempfile
import time
from urllib.error import HTTPError
from urllib.parse import urlparse
from urllib.request import Request, urlopen

from PIL import Image

from common import (EDGE_CATEGORIES, RAW_SUFFIXES, SEED, digest, file_hash,
                    local_file, output_directory, read_json, relative_file,
                    safe_id, sorted_samples, write_json)
from masks import union
from coco_attribution import load_snapshot, validate_snapshot
from raw_preview import embedded_preview
from verify_manifest import require, validate_rig, validate_samples, verify


USER_AGENT = "HappyPhoton-MLSPIKE-WP1/1.0 (https://github.com/seasalim/happy-photon)"


def download(url, destination, attempts=6, pause=1.0):
    """Polite, rate-limit-aware download: pause between requests; honor 429/503 Retry-After."""
    for attempt in range(attempts):
        time.sleep(pause)
        try:
            request = Request(url, headers={"User-Agent": USER_AGENT})
            with urlopen(request, timeout=60) as response, destination.open("wb") as stream:
                shutil.copyfileobj(response, stream)
            return
        except HTTPError as error:
            if error.code not in (429, 503) or attempt == attempts - 1:
                raise
            retry_after = error.headers.get("Retry-After", "")
            time.sleep(int(retry_after) if retry_after.isdigit() else 15 * (attempt + 1))


def acquire(sample, destination, local_root=None):
    """Copy only explicitly listed local public fixtures, or fetch their source URL."""
    destination.parent.mkdir(parents=True, exist_ok=True)
    if "local_path" in sample:
        require(local_root is not None, "A local pick requires --local-root")
        source = local_file(relative_file(local_root, sample["local_path"]))
        require(source.resolve() != destination.resolve(), "Refusing source/output collision")
        shutil.copyfile(source, destination)
    else:
        download(sample["url"], destination)
    actual = file_hash(destination)
    if sample.get("sha256"):
        require(actual == sample["sha256"], f"{sample['id']}: expected SHA-256 differs")
    return actual


def materialize(sample, root, local_root=None):
    result = {key: value for key, value in sample.items()
              if key not in {"mask_annotations", "local_path", "sha256"}}
    name = safe_id(sample["id"])
    suffix = Path(urlparse(sample["url"]).path).suffix.lower()
    require(suffix in RAW_SUFFIXES if sample["raw"]
            else suffix in {".jpg", ".jpeg", ".png", ".tif", ".tiff", ".webp"},
            f"{name}: unsupported source extension {suffix}")
    image_path = relative_file(root, f"images/{name}{suffix}")
    image_path.parent.mkdir(parents=True, exist_ok=True)
    if image_path.exists():
        require(bool(sample.get("sha256")),
                f"{name}: existing file requires a pinned SHA-256; use a fresh output directory")
        require(file_hash(image_path) == sample["sha256"], f"{name}: cached SHA-256 differs")
    else:
        # A failed download never becomes a valid cached original.
        with tempfile.TemporaryDirectory(dir=image_path.parent) as temporary:
            staged = Path(temporary) / ("source" + suffix)
            acquire(sample, staged, local_root)
            staged.replace(image_path)
    result.update(file=image_path.relative_to(root).as_posix(), sha256=file_hash(image_path))
    if sample["raw"]:
        preview = embedded_preview(image_path)
        preview_path = relative_file(root, f"previews/{name}.png")
        preview_path.parent.mkdir(parents=True, exist_ok=True)
        preview.save(preview_path, format="PNG")
        result.update(preview_file=preview_path.relative_to(root).as_posix(),
                      preview_sha256=file_hash(preview_path))
    else:
        with Image.open(local_file(image_path)) as image:
            image.load()
            require(image.size == (sample["width"], sample["height"]),
                    f"{name}: downloaded dimensions do not match selection")
    if sample["category"] not in EDGE_CATEGORIES:
        mask = union(sample["mask_annotations"], sample["width"], sample["height"])
        mask_path = relative_file(root, f"masks/{name}.png")
        mask_path.parent.mkdir(parents=True, exist_ok=True)
        Image.fromarray(mask * 255).save(mask_path, format="PNG")
        result.update(mask_file=mask_path.relative_to(root).as_posix(),
                      mask_sha256=file_hash(mask_path))
    return result


def licence_records(records, evidence_root, output):
    require(len(records) == 2 and {r["name"] for r in records} == {"instances", "stuff"},
            "Supply exactly two reviewed annotation licence records: instances and stuff")
    result = []
    for record in sorted(records, key=lambda r: r["name"]):
        require(record.get("reviewed") is True and record.get("licence") and record.get("url"),
                "Stop for host review of each annotation licence before fetching")
        source = relative_file(evidence_root, record["path"])
        require(file_hash(source) == record["sha256"], "Annotation licence evidence changed")
        target = relative_file(output, f"licences/{record['name']}.txt")
        target.parent.mkdir(parents=True, exist_ok=True)
        require(source.resolve() != target.resolve(), "Refusing licence source/output collision")
        shutil.copyfile(source, target)
        result.append({key: record[key] for key in
                       ("name", "url", "licence", "reviewed", "sha256")} |
                      {"file": target.relative_to(output).as_posix()})
    return result


def fetch(selection, edges, rig, licences, output, evidence_root,
          local_root=None, attribution=None, attribution_sha256=None):
    require(selection["schema_version"] == 1 and selection["seed"] == SEED,
            "Expected the seed-51 selection")
    samples = deepcopy(selection["samples"] + edges)
    validate_snapshot(attribution)
    require(bool(attribution_sha256) and
            selection.get("attribution_sha256") == attribution_sha256,
            "Attribution snapshot SHA-256 differs from selection; rerun selection")
    for sample in samples:
        if sample["source"] != "coco":
            continue
        extra = attribution.get(sample["source_id"])
        require(extra is not None and "unresolved" not in extra,
                f"{sample['id']}: resolved author attribution required")
        sample.update(extra)
    samples = sorted_samples(samples)
    validate_samples(samples)
    validate_rig(rig)
    output = Path(output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    require(not (output / "manifest.json").exists(),
            "Manifest already exists; use a fresh directory for an independent rerun")
    evidence = licence_records(licences, evidence_root, output)
    manifest = {
        "schema_version": 1, "seed": SEED, "annotations": selection["annotations"],
        "annotation_licences": evidence, "rig": rig,
        "attribution_sha256": attribution_sha256,
        "samples": [materialize(s, output, local_root) for s in samples],
    }
    revision = verify(manifest, output)
    write_json(output / "manifest.json", manifest)
    (output / "manifest.sha256").write_text(revision + "\n", encoding="ascii")
    return manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--labeled", type=Path, required=True)
    parser.add_argument("--edges", type=Path, required=True)
    parser.add_argument("--rig", type=Path, required=True)
    parser.add_argument("--annotation-licences", type=Path, required=True)
    parser.add_argument("--attribution", type=Path, required=True)
    parser.add_argument("--local-root", type=Path)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    attribution, snapshot_hash = load_snapshot(args.attribution)
    manifest = fetch(
        read_json(args.labeled), read_json(args.edges), read_json(args.rig),
        read_json(args.annotation_licences), output_directory(args.output_dir),
        args.annotation_licences.parent, args.local_root,
        attribution, snapshot_hash)
    print(digest(manifest))


if __name__ == "__main__":
    main()
