"""Extract the largest decodable embedded JPEG without decoding RAW pixels."""

from io import BytesIO

from PIL import Image

from common import local_file


def embedded_preview(path):
    data = local_file(path).read_bytes()
    best, best_area = None, 0
    start = data.find(b"\xff\xd8\xff")
    while start >= 0:
        # Pillow understands JPEG marker lengths (including nested EXIF JPEGs).
        try:
            with Image.open(BytesIO(data[start:])) as image:
                if image.format == "JPEG" and image.width * image.height > best_area:
                    image.load()
                    best = image.copy()
                    best_area = image.width * image.height
        except (OSError, ValueError, Image.DecompressionBombError):
            pass
        start = data.find(b"\xff\xd8\xff", start + 3)
    if best is None:
        raise ValueError(f"No decodable embedded JPEG preview in {path}")
    return best
