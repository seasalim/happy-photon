"""Local-only conversion evidence; no model code or weights are downloaded here."""

import hashlib
import json
import os
from pathlib import Path
import subprocess

os.environ["HF_HUB_OFFLINE"] = "1"
os.environ["TRANSFORMERS_OFFLINE"] = "1"


def file_hash(path):
    path = Path(path)
    attributes = getattr(path.stat(), "st_file_attributes", 0)
    if attributes & (0x1000 | 0x40000 | 0x400000):
        raise ValueError(f"Cloud/offline input refused: {path}")
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def verify_weights(path, expected):
    actual = file_hash(path)
    if actual != expected.lower():
        raise ValueError(f"Weights hash mismatch: {actual}")
    return actual


def verify_source(root, revision):
    root = Path(root).resolve()
    actual = subprocess.check_output(
        ["git", "-C", str(root), "rev-parse", "HEAD"], text=True).strip()
    if len(revision) != 40 or actual != revision:
        raise ValueError("Pass the full pinned source commit; checkout must match it")
    dirty = subprocess.check_output(
        ["git", "-C", str(root), "status", "--porcelain"], text=True)
    if dirty.strip():
        raise ValueError("Export source checkout must be clean")
    return actual


def new_output(path):
    path = Path(path).resolve()
    repo = Path(__file__).resolve().parents[3]
    if path == repo or repo in path.parents:
        raise ValueError("Keep model binaries outside the product repository")
    if path.exists():
        raise FileExistsError(path)
    if path.with_suffix(".evidence.json").exists():
        raise FileExistsError(path.with_suffix(".evidence.json"))
    if path.with_suffix(".json").exists():
        raise FileExistsError(path.with_suffix(".json"))
    path.parent.mkdir(parents=True, exist_ok=True)
    return path


def evidence(output, args, config, extra=None):
    config["model_sha256"] = file_hash(output)
    output.with_suffix(".json").write_text(
        json.dumps(config, indent=2) + "\n", encoding="utf-8")
    record = {
        "arguments": {key: str(value) for key, value in vars(args).items()},
        "output_sha256": config["model_sha256"],
        "output_bytes": output.stat().st_size,
        "calibration": "none",
        **(extra or {}),
    }
    with output.with_suffix(".evidence.json").open("x", encoding="utf-8") as stream:
        json.dump(record, stream, indent=2)
        stream.write("\n")


# IS-Net general-use is normalized with mean 0.5 / std 1.0 by its authors
# (DIS IS-Net/Inference.py:43); BiRefNet and U2-Net use ImageNet statistics.
UPSTREAM_NORMALIZATION = {"isnet": ([0.5, 0.5, 0.5], [1.0, 1.0, 1.0])}


def config(candidate, size, capability="subject", activation="probability"):
    mean, std = UPSTREAM_NORMALIZATION.get(
        candidate, ([0.485, 0.456, 0.406], [0.229, 0.224, 0.225]))
    return {
        "candidate": candidate, "capability": capability, "width": size, "height": size,
        "input_name": "image", "output_name": "mask", "input_layout": "NCHW",
        "output_layout": "NCHW", "activation": activation, "class_index": 0,
        "mean": mean, "std": std,
    }
