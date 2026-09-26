"""Render a local HTML contact sheet with ground truth only; no inference."""

import argparse
from html import escape
from pathlib import Path

from PIL import Image, ImageOps

from common import local_file, output_directory, read_json, relative_file
from verify_manifest import verify


def thumbnail(sample, root):
    source = sample.get("preview_file", sample["file"])
    with Image.open(local_file(relative_file(root, source))) as image:
        picture = image.convert("RGB")
        if sample["raw"]:
            picture = ImageOps.exif_transpose(image).convert("RGB")
    picture.thumbnail((480, 360), Image.Resampling.LANCZOS)
    if "mask_file" in sample:
        with Image.open(local_file(relative_file(root, sample["mask_file"]))) as image:
            mask = image.convert("L").resize(picture.size, Image.Resampling.NEAREST)
        # Ground truth is highlighted with a translucent fill, including empty negatives.
        overlay = Image.new("RGB", picture.size, "lime")
        picture = Image.composite(Image.blend(picture, overlay, 0.4), picture, mask)
    return picture


def generate(manifest, sample_root, output):
    revision = verify(manifest, sample_root)
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    thumbnails = output / "thumbnails"
    thumbnails.mkdir(exist_ok=True)
    cards = []
    for sample in manifest["samples"]:
        name = sample["id"]
        thumbnail(sample, sample_root).save(thumbnails / f"{name}.png", format="PNG")
        description = ("Embedded RAW preview" if sample["raw"] else
                       "Ground-truth overlay" if "mask_file" in sample else "Edge image")
        fields = [sample["category"], sample["licence"], sample["author"],
                  ", ".join(sample["tags"]), description]
        caption = "<br>".join(escape(field) for field in fields if field)
        cards.append(
            f'<figure><img src="thumbnails/{name}.png" alt="{escape(name)}">'
            f'<figcaption><strong>{escape(name)}</strong><br>{caption}<br>'
            f'<a href="{escape(sample["url"], quote=True)}">Public source</a>'
            '</figcaption></figure>')
    html = (
        '<!doctype html><html lang="en"><meta charset="utf-8">'
        '<meta name="viewport" content="width=device-width, initial-scale=1">'
        '<title>MLSPIKE frozen sample review</title>'
        '<style>body{font-family:system-ui;margin:2rem}main{display:grid;'
        'grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:1rem}'
        'figure{margin:0;padding:1rem;border:1px solid;overflow-wrap:anywhere}'
        'img{max-width:100%;height:auto}code{overflow-wrap:anywhere}</style>'
        '<h1>MLSPIKE sample-set review</h1>'
        f'<p>Revision: <code>{revision}</code></p>'
        '<p>Local evaluation only. Do not redistribute. No model has run. '
        'Green overlays show labeled ground truth. Edge images have no prediction. '
        'RAW dimensions and visual sub-quota labels require reviewer confirmation.</p>'
        '<main>' + "".join(cards) + '</main></html>')
    path = output / "contact-sheet.html"
    path.write_text(html, encoding="utf-8")
    return path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    args = parser.parse_args()
    print(generate(read_json(args.manifest), args.manifest.parent,
                   output_directory(args.output_dir)))


if __name__ == "__main__":
    main()
