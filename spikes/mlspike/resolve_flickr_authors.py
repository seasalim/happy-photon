"""Host-only Flickr attribution resolution; never called by selection or fetching."""

import argparse
from http.cookiejar import CookieJar
from pathlib import Path
import re
from urllib.error import URLError
from urllib.parse import parse_qs, unquote, urlsplit
from urllib.request import HTTPCookieProcessor, Request, build_opener

from common import read_json, write_json
from coco_attribution import load_snapshot, validate_snapshot


def photo_id(url):
    parsed = urlsplit(url)
    host = parsed.hostname or ""
    if host in {"flickr.com", "www.flickr.com"}:
        if parsed.path == "/photo.gne":
            value = parse_qs(parsed.query).get("id", [""])[0]
            if re.fullmatch(r"[0-9]+", value):
                return value
        match = re.fullmatch(r"/photos/[^/]+/([0-9]+)/?", parsed.path)
    elif host == "staticflickr.com" or host.endswith(".staticflickr.com"):
        match = re.search(r"/([0-9]+)_[^/]+\.[^/]+$", parsed.path)
    else:
        match = None
    if match:
        return match[1]
    raise ValueError("No Flickr photo id in source URL")


def parse_owner(url, expected_id):
    parsed = urlsplit(url)
    if parsed.scheme not in {"http", "https"} or parsed.hostname not in {
        "flickr.com", "www.flickr.com"
    }:
        return {"unresolved": "Flickr redirect did not reach a Flickr photo page"}
    match = re.fullmatch(r"/photos/([^/]+)/([0-9]+)/?", parsed.path)
    if not match or match[2] != expected_id:
        return {"unresolved": "Flickr photo page has no recoverable owner or matching photo id"}
    owner = unquote(match[1])
    if not owner.strip() or "/" in owner:
        return {"unresolved": "Flickr photo page has no recoverable owner"}
    return {"author": owner,
            "attribution_url": f"https://www.flickr.com/photos/{match[1]}/{expected_id}/"}


def resolve(url, opener):
    try:
        identifier = photo_id(url)
        target = f"https://www.flickr.com/photo.gne?id={identifier}"
        # urllib follows HTTP redirects. Reopen an rb landing page using the same
        # cookie jar when Flickr's cookie bootstrap finishes without a redirect.
        for _ in range(3):
            request = Request(target, headers={"User-Agent": "HappyPhoton-MLSPIKE-WP1/1.0"})
            with opener.open(request, timeout=60) as response:
                target = response.geturl()
            parsed = urlsplit(target)
            if parsed.hostname not in {"flickr.com", "www.flickr.com"} or parsed.path != "/photo.gne":
                return parse_owner(target, identifier)
        return {"unresolved": "Flickr cookie redirect did not reach a photo owner"}
    except (OSError, URLError, ValueError) as error:
        return {"unresolved": f"{type(error).__name__}: {error}"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--needs", type=Path, required=True)
    parser.add_argument("--snapshot", type=Path, required=True)
    args = parser.parse_args()
    snapshot, _ = load_snapshot(args.snapshot)
    opener = build_opener(HTTPCookieProcessor(CookieJar()))
    for item in read_json(args.needs):
        image_id = str(item["image_id"])
        if image_id in snapshot:
            continue  # Preserve reviewed resolved and unresolved decisions.
        snapshot[image_id] = resolve(item["flickr_url"], opener)
        validate_snapshot(snapshot)
        write_json(args.snapshot, snapshot)
        print(f"{image_id}: {snapshot[image_id]}")


if __name__ == "__main__":
    main()
