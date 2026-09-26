"""Hosted-runner fetch of the frozen sample set by its canonical manifest.

Downloads every image from its public source URL and verifies its SHA-256; masks come
from the maintainer-only release bundle (masks.zip), never from a public location.
Images are used for local evaluation only and are never uploaded as artifacts (D-6).
"""

import argparse
import hashlib
import json
import time
import urllib.error
import urllib.request
import zipfile
from pathlib import Path

USER_AGENT = "HappyPhoton-MLSPIKE-WP2/1.0 (https://github.com/seasalim/happy-photon)"


def download(url, destination, attempts=6):
    for attempt in range(attempts):
        time.sleep(1.0)
        try:
            request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
            with urllib.request.urlopen(request, timeout=120) as response:
                destination.write_bytes(response.read())
            return
        except urllib.error.HTTPError as error:
            if error.code not in (429, 503) or attempt == attempts - 1:
                raise
            retry = error.headers.get("Retry-After", "")
            time.sleep(int(retry) if retry.isdigit() else 15 * (attempt + 1))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--masks", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    expected = hashlib.sha256(args.manifest.read_bytes()).hexdigest()
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "manifest.json").write_bytes(args.manifest.read_bytes())
    for sample in manifest["samples"]:
        target = args.output / sample["file"]
        target.parent.mkdir(parents=True, exist_ok=True)
        download(sample["url"], target)
        actual = hashlib.sha256(target.read_bytes()).hexdigest()
        if actual != sample["sha256"]:
            raise SystemExit(f"{sample['id']}: SHA-256 {actual} != {sample['sha256']}")
    with zipfile.ZipFile(args.masks) as archive:
        archive.extractall(args.output)
    for sample in manifest["samples"]:
        if sample.get("mask_file"):
            actual = hashlib.sha256((args.output / sample["mask_file"]).read_bytes()).hexdigest()
            if actual != sample["mask_sha256"]:
                raise SystemExit(f"{sample['id']}: mask SHA-256 mismatch")
    print(f"frozen set ready: {len(manifest['samples'])} images, manifest {expected}")


if __name__ == "__main__":
    main()
