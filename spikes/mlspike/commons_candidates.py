"""Collect Commons candidates for human review; this never chooses the edge set."""

import argparse
from html.parser import HTMLParser
import json
from pathlib import Path
from urllib.parse import urlencode
from urllib.request import Request, urlopen

from common import (EDGE_CATEGORIES, RAW_SUFFIXES, licence_name, output_directory,
                    read_json, write_json)

API = "https://commons.wikimedia.org/w/api.php"
QUERIES = {
    "portrait": "portrait backlit hair",
    "pet": "cat dog long fur",
    "product": "product object still life",
    "low-light": "high ISO low light",
    "multi-subject": "group people person pet",
    "landscape": "landscape sky branches wires sunset haze",
}


class PlainText(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []

    def handle_data(self, data):
        self.parts.append(data)


def plain(value):
    parser = PlainText()
    parser.feed(value)
    return " ".join("".join(parser.parts).split())


def parse_pages(response, category):
    result = []
    pages = response.get("query", {}).get("pages", {})
    if isinstance(pages, dict):
        pages = pages.values()
    for page in pages:
        for info in page.get("imageinfo", []):
            if max(info.get("width", 0), info.get("height", 0)) < 2400:
                continue
            metadata = info.get("extmetadata", {})
            try:
                licence = licence_name(metadata.get("LicenseShortName", {}).get("value", ""))
            except ValueError:
                continue
            author = plain(metadata.get("Artist", {}).get("value", ""))
            if not author:
                continue
            title = page["title"]
            result.append({
                "id": f"commons-{page['pageid']}", "source": "commons",
                "source_id": str(page["pageid"]), "title": title,
                "url": info["url"], "attribution_url": info["descriptionurl"],
                "author": author, "licence": licence,
                "licence_url": metadata.get("LicenseUrl", {}).get("value", ""),
                "category": category, "width": info["width"], "height": info["height"],
                "raw": Path(title).suffix.lower() in RAW_SUFFIXES, "tags": [],
            })
    return result


def query(category, search, limit, user_agent):
    candidates = {}
    continuation = {}
    while len(candidates) < limit:
        parameters = {
            "action": "query", "format": "json", "generator": "search",
            "gsrsearch": search, "gsrnamespace": 6, "gsrlimit": 50,
            "prop": "imageinfo", "iiprop": "url|size|extmetadata",
            **continuation,
        }
        request = Request(API + "?" + urlencode(parameters),
                          headers={"User-Agent": user_agent})
        with urlopen(request, timeout=60) as stream:
            response = json.load(stream)
        if "error" in response:
            raise ValueError(f"Commons API error: {response['error']}")
        for sample in parse_pages(response, category):
            candidates[sample["id"]] = sample
        continuation = response.get("continue")
        if not continuation:
            break
    return sorted(candidates.values(), key=lambda item: item["id"])[:limit]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--category", choices=EDGE_CATEGORIES, required=True)
    parser.add_argument("--query", help="Override the default search for this review batch")
    parser.add_argument("--limit", type=int, default=50)
    parser.add_argument("--user-agent", help="Descriptive contact-bearing Wikimedia User-Agent")
    parser.add_argument("--response", type=Path, help="Process a saved API response offline")
    args = parser.parse_args()
    if args.limit <= 0:
        parser.error("--limit must be positive")
    search = args.query or QUERIES[args.category]
    if args.response:
        candidates = parse_pages(read_json(args.response), args.category)
    else:
        if not args.user_agent:
            parser.error("--user-agent is required for a live query")
        candidates = query(args.category, search, args.limit, args.user_agent)
    write_json(output_directory(args.output_dir) / f"candidates-{args.category}.json",
               {"category": args.category, "query": search, "candidates": candidates})


if __name__ == "__main__":
    main()
