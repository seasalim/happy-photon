"""Shared manifest conventions for the local-only MLSPIKE sample rig."""

import hashlib
import json
import re
from pathlib import Path
from urllib.parse import urlparse

SEED = 51
QUOTAS = {
    "subject-person": 16, "subject-animal": 8,
    "sky": 16, "sky-negative": 4,
    "portrait": 10, "pet": 6, "product": 4, "low-light": 4,
    "multi-subject": 4, "landscape": 8,
}
EDGE_CATEGORIES = tuple(list(QUOTAS)[4:])
ANIMALS = {"bird", "cat", "dog", "horse", "sheep", "cow", "elephant",
           "bear", "zebra", "giraffe"}
RAW_SUFFIXES = {".nef", ".cr2", ".cr3", ".dng", ".raf", ".arw", ".rw2",
                ".orf", ".pef", ".srw", ".raw"}
SUBQUOTAS = {
    "portrait": {"backlit-or-flyaway": 4, "dark-on-dark": 2},
    "pet": {"long-fur": 2},
    "landscape": {"branches": 3, "wires": 1, "sunset": 1, "hazy-horizon": 1},
}


def canonical_bytes(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=False,
                       separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")


def digest(value):
    return hashlib.sha256(canonical_bytes(value)).hexdigest()


def local_file(path):
    """Do not hydrate a Windows offline/recall-on-open/recall-on-data-access file."""
    path = Path(path)
    attributes = getattr(path.stat(), "st_file_attributes", 0)
    if attributes & (0x1000 | 0x40000 | 0x400000):
        raise ValueError(f"Cloud/offline file is not an approved local input: {path}")
    return path


def file_hash(path):
    with local_file(path).open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def read_json(path):
    return json.loads(local_file(path).read_text(encoding="utf-8-sig"))


def write_json(path, value):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(canonical_bytes(value))


def output_directory(path):
    path = Path(path).resolve()
    repo = Path(__file__).resolve().parents[2]
    if path == repo or repo in path.parents:
        raise ValueError("Sample data and contact sheets must stay outside the repo")
    path.mkdir(parents=True, exist_ok=True)
    return path


def relative_file(root, name):
    path = (Path(root) / name).resolve()
    if Path(name).is_absolute() or Path(root).resolve() not in path.parents:
        raise ValueError(f"Not a sample-relative file: {name}")
    return path


def safe_id(value):
    if not isinstance(value, str) or not re.fullmatch(r"[a-zA-Z0-9_-]+", value):
        raise ValueError(f"Unsafe sample identifier: {value!r}")
    return value


def licence_name(value):
    """Normalize only explicit allowed grants; never turn NC/ND into BY."""
    value = value.strip()
    normalized = value.lower().replace("_", " ").replace("-", " ")
    normalized = " ".join(normalized.split())
    names = {
        "cc0": "CC0-1.0", "cc0 1.0": "CC0-1.0",
        "public domain": "Public-domain",
        "no known copyright restrictions": "No-known-copyright-restrictions",
        "united states government work": "US-Government",
        "us government": "US-Government",
    }
    if normalized in names:
        return names[normalized]
    match = re.fullmatch(r"cc by( sa)? (1\.0|2\.0|2\.5|3\.0|4\.0)", normalized)
    if match:
        return "CC-BY" + ("-SA" if match[1] else "") + "-" + match[2]
    parsed = urlparse(value)
    if parsed.hostname in {"creativecommons.org", "www.creativecommons.org"}:
        match = re.fullmatch(r"/licenses/(by|by-sa)/(1\.0|2\.0|2\.5|3\.0|4\.0)/?", parsed.path)
        if match:
            return "CC-" + match[1].upper() + "-" + match[2]
        if parsed.path.rstrip("/") == "/publicdomain/zero/1.0":
            return "CC0-1.0"
        if parsed.path.rstrip("/") == "/publicdomain/mark/1.0":
            return "Public-domain"
    if value == "No-known-copyright-restrictions":
        return value
    if value == "Public-domain":
        return value
    raise ValueError(f"Disallowed or unrecognized image licence: {value!r}")


def coco_licences(document):
    result = {}
    for item in document["licenses"]:
        for candidate in (item.get("url", ""), item.get("name", "")):
            try:
                name = licence_name(candidate)
            except ValueError:
                continue
            if name.startswith("CC-BY-") or name in {
                "No-known-copyright-restrictions", "US-Government"
            }:
                result[item["id"]] = name
                break
    return result


def sorted_samples(samples):
    return sorted(samples, key=lambda item: (item["category"], item["id"]))
