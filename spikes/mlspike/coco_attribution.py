"""Validate the reviewed COCO image-id attribution snapshot."""

import hashlib
import json

from common import local_file


def validate_snapshot(snapshot):
    if not isinstance(snapshot, dict):
        raise ValueError("Attribution snapshot must be keyed by COCO image id")
    for image_id, entry in snapshot.items():
        if not isinstance(image_id, str) or not image_id.isascii() or not image_id.isdigit():
            raise ValueError("Attribution keys must be numeric COCO image ids")
        if str(int(image_id)) != image_id or not isinstance(entry, dict):
            raise ValueError(f"Invalid attribution entry: {image_id}")
        if set(entry) == {"unresolved"}:
            fields = ("unresolved",)
        elif set(entry) == {"author", "attribution_url"}:
            fields = ("author", "attribution_url")
        else:
            raise ValueError(f"{image_id}: expected author/attribution_url or unresolved")
        if any(not isinstance(entry[k], str) or not entry[k].strip() for k in fields):
            raise ValueError(f"{image_id}: attribution fields must be nonempty strings")
    return snapshot


def load_snapshot(path):
    data = local_file(path).read_bytes()
    return validate_snapshot(json.loads(data.decode("utf-8-sig"))), hashlib.sha256(data).hexdigest()
