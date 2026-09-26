"""COCO masks without pycocotools; polygons sample pixel centers."""

import numpy as np


def rle_counts(encoded):
    if isinstance(encoded, list):
        if any(type(value) is not int or value < 0 for value in encoded):
            raise ValueError("RLE runs must be nonnegative integers")
        return encoded
    if not isinstance(encoded, str):
        raise ValueError("RLE counts must be a list or COCO compressed string")
    counts, position = [], 0
    while position < len(encoded):
        value, shift = 0, 0
        while True:
            if position >= len(encoded):
                raise ValueError("Truncated compressed RLE")
            chunk = ord(encoded[position]) - 48
            position += 1
            if not 0 <= chunk <= 63 or shift > 60:
                raise ValueError("Invalid compressed RLE")
            value |= (chunk & 31) << shift
            shift += 5
            if not chunk & 32:
                if chunk & 16:
                    value |= -1 << shift
                break
        if len(counts) > 2:
            value += counts[-2]
        if value < 0:
            raise ValueError("Negative RLE run")
        counts.append(value)
    return counts


def decode(segmentation, width, height):
    if width <= 0 or height <= 0:
        raise ValueError("Invalid mask dimensions")
    mask = np.zeros((height, width), dtype=np.uint8)
    if isinstance(segmentation, dict):
        if segmentation["size"] != [height, width]:
            raise ValueError("RLE dimensions differ from image")
        counts = rle_counts(segmentation["counts"])
        if sum(counts) != width * height:
            raise ValueError("RLE does not cover the image")
        flat = np.zeros(width * height, dtype=np.uint8)
        offset = 0
        for index, count in enumerate(counts):
            if index % 2:
                flat[offset:offset + count] = 1
            offset += count
        return flat.reshape((height, width), order="F")
    if not isinstance(segmentation, list):
        raise ValueError("Expected COCO polygon list or RLE")
    for polygon in segmentation:
        points = np.asarray(polygon, dtype=float)
        if len(points) < 6 or len(points) % 2 or not np.isfinite(points).all():
            raise ValueError("Invalid polygon")
        points = points.reshape((-1, 2))
        # Union separate polygons. Half-open scanlines avoid counting vertices twice.
        for row in range(max(0, int(np.floor(points[:, 1].min()))),
                         min(height, int(np.ceil(points[:, 1].max())))):
            y = row + 0.5
            crossings = []
            for a, b in zip(points, np.roll(points, -1, axis=0)):
                if (a[1] <= y < b[1]) or (b[1] <= y < a[1]):
                    crossings.append(a[0] + (y - a[1]) * (b[0] - a[0]) / (b[1] - a[1]))
            crossings.sort()
            for left, right in zip(crossings[::2], crossings[1::2]):
                start = max(0, int(np.ceil(left - 0.5)))
                stop = max(0, min(width, int(np.ceil(right - 0.5))))
                mask[row, start:stop] = 1
    return mask


def union(annotations, width, height):
    result = np.zeros((height, width), dtype=np.uint8)
    for item in annotations:
        result |= decode(item["segmentation"], width, height)
    return result


def adjacent(first, second):
    return bool(np.any(first[:-1] & second[1:]) or
                np.any(first[1:] & second[:-1]) or
                np.any(first[:, :-1] & second[:, 1:]) or
                np.any(first[:, 1:] & second[:, :-1]))
