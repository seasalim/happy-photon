"""Validate G2 quotas, attribution, file hashes and an independent rerun."""

import argparse
from collections import Counter
from pathlib import Path
from urllib.parse import unquote, urlparse

import numpy as np
from PIL import Image

from common import (EDGE_CATEGORIES, QUOTAS, RAW_SUFFIXES, SEED, SUBQUOTAS,
                    digest, file_hash, licence_name, local_file, read_json,
                    relative_file, safe_id, sorted_samples)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def validate_samples(samples, fetched=False):
    require(Counter(s["category"] for s in samples) == Counter(QUOTAS),
            "Category counts must equal " + str(QUOTAS))
    require(len({s["id"] for s in samples}) == len(samples), "Duplicate sample identifiers")
    sources = [(s["source"], s["source_id"]) for s in samples]
    require(len(set(sources)) == len(sources), "One source cannot fill multiple sample slots")
    require(len({s["url"] for s in samples}) == len(samples), "Duplicate source URLs")
    for sample in samples:
        name = safe_id(sample["id"])
        category = sample["category"]
        licence = licence_name(sample["licence"])
        require(licence == sample["licence"], f"{name}: licence must be normalized")
        require(isinstance(sample["raw"], bool), f"{name}: raw must be a boolean")
        require(all(type(sample[k]) is int and sample[k] > 0 for k in ("width", "height")),
                f"{name}: invalid dimensions")
        require(isinstance(sample["tags"], list) and
                all(isinstance(tag, str) for tag in sample["tags"]), f"{name}: invalid tags")
        require(isinstance(sample["source_id"], str) and sample["source_id"].strip(),
                f"{name}: missing source identifier")
        parsed = urlparse(sample["url"])
        require(parsed.scheme in {"http", "https"} and parsed.hostname,
                f"{name}: public source URL required")
        require(isinstance(sample["author"], str) and sample["author"].strip(),
                f"{name}: author attribution required")
        if category in EDGE_CATEGORIES:
            require(max(sample["width"], sample["height"]) >= 2400,
                    f"{name}: edge image long edge is below 2400")
            require(sample["source"] in {"commons", "raw.pixls.us", "repo"},
                    f"{name}: unsupported edge source")
            if sample["source"] == "commons":
                require(parsed.hostname == "upload.wikimedia.org",
                        f"{name}: Commons image URL must be on upload.wikimedia.org")
            elif sample["source"] == "raw.pixls.us":
                require(parsed.hostname == "raw.pixls.us" and licence == "CC0-1.0",
                        f"{name}: raw.pixls.us requires its public URL and CC0")
            else:
                require(parsed.hostname == "raw.githubusercontent.com" and
                        parsed.path.startswith("/seasalim/happy-photon/") and
                        "/Tests/assets/" in unquote(parsed.path) and sample["raw"],
                        f"{name}: repo picks must identify public Tests/assets RAWs")
            if sample["raw"]:
                require(bool(sample.get("dimensions_evidence")),
                        f"{name}: RAW dimensions need reviewed source evidence")
        else:
            require(sample["source"] == "coco" and not sample["raw"] and
                    parsed.hostname == "images.cocodataset.org" and
                    parsed.path.startswith("/val2017/"),
                    f"{name}: labeled samples must be COCO val2017 raster images")
            require(licence.startswith("CC-BY-") or licence in {
                "No-known-copyright-restrictions", "US-Government"},
                f"{name}: licence outside COCO allowed set")
            evidence = sample["evidence"]
            if category.startswith("subject-"):
                require(max(sample["width"], sample["height"]) >= 600 and
                        evidence["noncrowd_targets"] == 1 and
                        0.15 <= evidence["subject_fraction"] <= 1 and
                        0 <= evidence["other_fraction"] <= 0.05,
                        f"{name}: subject coverage rules fail")
            elif category == "sky":
                require(0.10 <= evidence["sky_fraction"] <= 0.70,
                        f"{name}: sky coverage rules fail")
                if "tree-next-to-sky" in sample["tags"]:
                    require(evidence["tree_fraction"] >= 0.05 and
                            evidence["tree_adjacent"] is True,
                            f"{name}: tree adjacency evidence missing")
            else:
                require(evidence["sky_fraction"] == 0 and evidence["indoor_fraction"] > 0,
                        f"{name}: negative must have no sky and indoor evidence")
        if fetched:
            require(len(sample["sha256"]) == 64, f"{name}: missing image hash")
            require((Path(sample["file"]).suffix.lower() in RAW_SUFFIXES) == sample["raw"],
                    f"{name}: RAW flag and file extension differ")
    for category, rules in SUBQUOTAS.items():
        for tag, minimum in rules.items():
            count = sum(s["category"] == category and tag in s["tags"] for s in samples)
            require(count >= minimum, f"{category}: {tag} needs {minimum}, got {count}")
    require(sum(s["raw"] for s in samples) >= 6, "At least six edge samples must be RAW")
    require(sum(s["category"] == "sky" and "tree-next-to-sky" in s["tags"]
                for s in samples) >= 6, "At least six sky samples need adjacent trees")


def validate_rig(rig):
    expected = {"windows-2025": ("x64", 4), "ubuntu-24.04": ("x64", 4),
                "macos-15": ("arm64", 3)}
    require(set(rig) == set(expected), "Rig must name all three reference environments")
    for name, (architecture, cores) in expected.items():
        record = rig[name]
        require(record.get("runner_image") and record.get("cpu_model"),
                f"{name}: runner image and CPU model are required")
        require(record.get("architecture") == architecture and record.get("cores") == cores,
                f"{name}: unexpected runner architecture/core count")
        gib = record["ram_bytes"] / (1024 ** 3)
        low, high = (6.5, 7.5) if architecture == "arm64" else (15, 17)
        require(low <= gib <= high, f"{name}: unexpected runner RAM")
        if architecture == "arm64":
            require("m1" in record["cpu_model"].lower(), f"{name}: expected M1-class CPU")


def verify(manifest, root):
    require(manifest["schema_version"] == 1 and manifest["seed"] == SEED,
            "Unsupported manifest schema or seed")
    samples = manifest["samples"]
    require(samples == sorted_samples(samples), "Manifest sample list is not sorted")
    validate_samples(samples, fetched=True)
    require(len({s["sha256"] for s in samples}) == len(samples),
            "Duplicate image bytes cannot fill separate sample slots")
    validate_rig(manifest["rig"])
    require(set(manifest["annotations"]) == {"instances", "stuff"} and
            all(isinstance(value, str) and len(value) == 64 and
                all(c in "0123456789abcdef" for c in value)
                for value in manifest["annotations"].values()),
            "Missing or malformed annotation source hashes")
    require({r["name"] for r in manifest["annotation_licences"]} == {"instances", "stuff"},
            "Both annotation licence records are required")
    for evidence in manifest["annotation_licences"]:
        require(evidence["reviewed"] is True and evidence["licence"] and evidence["url"],
                "Annotation licences require explicit host review")
        path = relative_file(root, evidence["file"])
        require(file_hash(path) == evidence["sha256"], "Annotation licence hash mismatch")
    for sample in samples:
        name = sample["id"]
        path = relative_file(root, sample["file"])
        require(file_hash(path) == sample["sha256"], f"{name}: source hash mismatch")
        if sample["raw"]:
            path = relative_file(root, sample["preview_file"])
            require(file_hash(path) == sample["preview_sha256"], f"{name}: preview hash mismatch")
        with Image.open(local_file(path)) as image:
            image.load()
            if not sample["raw"]:
                require(image.size == (sample["width"], sample["height"]),
                        f"{name}: image dimensions differ")
        if sample["category"] not in EDGE_CATEGORIES:
            mask_path = relative_file(root, sample["mask_file"])
            require(file_hash(mask_path) == sample["mask_sha256"], f"{name}: mask hash mismatch")
            with Image.open(local_file(mask_path)) as mask_image:
                require(mask_image.size == (sample["width"], sample["height"]),
                        f"{name}: mask dimensions differ")
                values = np.asarray(mask_image)
                require(np.isin(values, [0, 255]).all(), f"{name}: mask is not binary")
                if sample["category"] == "sky-negative":
                    require(not values.any(), f"{name}: negative contains sky pixels")
                elif sample["category"] == "sky":
                    require(0.10 <= float((values > 0).mean()) <= 0.70,
                            f"{name}: mask sky coverage differs")
    return digest(manifest)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--compare", type=Path, required=True,
                        help="Manifest from independent selection/fetch rerun")
    args = parser.parse_args()
    hashes = []
    for path in (args.manifest, args.compare):
        value = verify(read_json(path), path.parent)
        expected = local_file(path.with_suffix(".sha256")).read_text().strip()
        require(value == expected, f"{path}: manifest hash sidecar differs")
        require(file_hash(path) == value, f"{path}: manifest is not canonical JSON")
        hashes.append(value)
    require(hashes[0] == hashes[1], "Independent rerun produced a different manifest hash")
    require(args.manifest.resolve() != args.compare.resolve(),
            "--compare must be an independent rerun manifest")
    print(f"G2 PASS: all quotas, licences, file integrity and rerun hash: {hashes[0]}")


if __name__ == "__main__":
    main()
