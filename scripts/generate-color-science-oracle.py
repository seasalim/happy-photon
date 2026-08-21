#!/usr/bin/env python3
"""Regenerate the committed NEWRAW colour-science reference vectors."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import colour
import numpy as np


COLOUR_SCIENCE_VERSION = "0.4.7"
NUMPY_VERSION = "2.4.4"
CHECKER_NAME = "ColorChecker24 - Before November 2014"
SAMPLES = np.array(
    [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [0.0, 0.0, 1.0],
        [1.0, 1.0, 1.0],
        [0.18, 0.50, 0.90],
    ],
    dtype=float,
)
EOTF_SAMPLES = np.array([0.0, 0.003, 0.04045, 0.18, 0.5, 1.0])
CAMERA_TO_SRGB = np.array(
    [
        [1.60, -0.70, 0.10],
        [-0.10, 1.30, -0.20],
        [0.05, -0.40, 1.35],
    ],
    dtype=float,
)
OKLAB_ENCODED_SAMPLES = np.array(
    [
        [0.10, 0.20, 0.70],
        [0.82, 0.31, 0.18],
        [0.50, 0.50, 0.50],
        [0.02, 0.025, 0.03],
        [0.95, 0.15, 0.80],
    ],
    dtype=float,
)
OKLAB_PROJECTION_CASES = (
    ([0.92, 0.15, 0.05], 2.0),
    ([0.10, 0.85, 0.20], 2.0),
    ([0.10, 0.20, 0.95], 2.0),
)


def array(value: np.ndarray) -> list:
    return np.asarray(value, dtype=float).tolist()


def colour_space(identifier: str, library_name: str) -> dict:
    space = colour.RGB_COLOURSPACES[library_name]
    to_xyz = np.asarray(space.matrix_RGB_to_XYZ, dtype=float)
    from_xyz = np.asarray(space.matrix_XYZ_to_RGB, dtype=float)
    return {
        "id": identifier,
        "libraryName": library_name,
        "primaries": array(space.primaries),
        "whitePoint": array(space.whitepoint),
        "matrixRgbToXyz": array(to_xyz),
        "matrixXyzToRgb": array(from_xyz),
        "roundTrips": [
            {
                "rgb": array(rgb),
                "xyz": array(to_xyz @ rgb),
                "recoveredRgb": array(from_xyz @ (to_xyz @ rgb)),
            }
            for rgb in SAMPLES
        ],
    }


def adaptation(identifier: str, source_xy: np.ndarray, destination_xy: np.ndarray) -> dict:
    source_xyz = colour.xy_to_XYZ(source_xy)
    destination_xyz = colour.xy_to_XYZ(destination_xy)
    matrix = colour.adaptation.matrix_chromatic_adaptation_VonKries(
        source_xyz,
        destination_xyz,
        transform="Bradford",
    )
    return {
        "id": identifier,
        "sourceWhite": array(source_xy),
        "destinationWhite": array(destination_xy),
        "matrix": array(matrix),
        "sourceWhiteXyz": array(source_xyz),
        "adaptedWhiteXyz": array(matrix @ source_xyz),
    }


def color_checker() -> dict:
    checker = colour.CCS_COLOURCHECKERS[CHECKER_NAME]
    patches = []
    for index, (name, xyy) in enumerate(checker.data.items()):
        xyz = colour.xyY_to_XYZ(xyy)
        patches.append(
            {
                "index": index,
                "name": name,
                "xyY": array(xyy),
                "xyz": array(xyz),
                "lab": array(colour.XYZ_to_Lab(xyz, illuminant=checker.illuminant)),
            }
        )
    return {
        "dataset": CHECKER_NAME,
        "observer": "CIE 1931 2 Degree Standard Observer",
        "illuminant": array(checker.illuminant),
        "referenceWhiteXyz": array(colour.xy_to_XYZ(checker.illuminant)),
        "rows": checker.rows,
        "columns": checker.columns,
        "patches": patches,
    }


def camera_characterization() -> dict:
    srgb = colour.RGB_COLOURSPACES["sRGB"]
    rec2020 = colour.RGB_COLOURSPACES["ITU-R BT.2020"]
    srgb_to_rec2020 = (
        np.asarray(rec2020.matrix_XYZ_to_RGB, dtype=float)
        @ np.asarray(srgb.matrix_RGB_to_XYZ, dtype=float)
    )
    camera_to_rec2020 = srgb_to_rec2020 @ CAMERA_TO_SRGB
    return {
        "id": "synthetic-camera-rgb",
        "cameraToSrgb": array(CAMERA_TO_SRGB),
        "cameraToRec2020": array(camera_to_rec2020),
        "samples": [
            {
                "cameraRgb": array(rgb),
                "rec2020": array(camera_to_rec2020 @ rgb),
            }
            for rgb in SAMPLES
        ],
    }


def to_oklch(lab: np.ndarray) -> np.ndarray:
    hue = np.arctan2(lab[2], lab[1]) % (2 * np.pi)
    return np.array([lab[0], np.hypot(lab[1], lab[2]), hue])


def from_oklch(lch: np.ndarray) -> np.ndarray:
    return np.array(
        [lch[0], lch[1] * np.cos(lch[2]), lch[1] * np.sin(lch[2])]
    )


def oklab_vectors() -> dict:
    from colour.models.oklab import (  # pylint: disable=import-outside-toplevel
        MATRIX_1_XYZ_TO_LMS,
        MATRIX_2_LMS_TO_LAB,
    )

    rec2020 = colour.RGB_COLOURSPACES["ITU-R BT.2020"]
    to_xyz = np.asarray(rec2020.matrix_RGB_to_XYZ, dtype=float)
    from_xyz = np.asarray(rec2020.matrix_XYZ_to_RGB, dtype=float)
    rec2020_to_lms = MATRIX_1_XYZ_TO_LMS @ to_xyz
    lms_to_rec2020 = from_xyz @ np.linalg.inv(MATRIX_1_XYZ_TO_LMS)

    rgb_vectors = []
    for encoded in OKLAB_ENCODED_SAMPLES:
        linear = colour.models.eotf_sRGB(encoded)
        xyz = to_xyz @ linear
        lab = colour.XYZ_to_Oklab(xyz)
        recovered = from_xyz @ colour.Oklab_to_XYZ(lab)
        rgb_vectors.append(
            {
                "encodedRec2020": array(encoded),
                "linearRec2020": array(linear),
                "oklab": array(lab),
                "oklch": array(to_oklch(lab)),
                "recoveredLinearRec2020": array(recovered),
            }
        )

    projection_vectors = []
    for encoded, scale in OKLAB_PROJECTION_CASES:
        linear = colour.models.eotf_sRGB(np.asarray(encoded, dtype=float))
        source = to_oklch(colour.XYZ_to_Oklab(to_xyz @ linear))
        target = source.copy()
        target[1] *= scale
        unprojected = from_xyz @ colour.Oklab_to_XYZ(from_oklch(target))
        low = 0.0
        high = float(target[1])
        for _ in range(80):
            middle = (low + high) * 0.5
            candidate = target.copy()
            candidate[1] = middle
            rgb = from_xyz @ colour.Oklab_to_XYZ(from_oklch(candidate))
            if np.all((rgb >= 0) & (rgb <= 1)):
                low = middle
            else:
                high = middle
        projected = target.copy()
        projected[1] = low
        projected_rgb = from_xyz @ colour.Oklab_to_XYZ(from_oklch(projected))
        projection_vectors.append(
            {
                "sourceEncodedRec2020": encoded,
                "targetOklch": array(target),
                "unprojectedLinearRec2020": array(unprojected),
                "projectedOklch": array(projected),
                "projectedLinearRec2020": array(projected_rgb),
            }
        )

    return {
        "matrixRec2020ToLms": array(rec2020_to_lms),
        "matrixLmsToRec2020": array(lms_to_rec2020),
        "matrixLmsToOklab": array(MATRIX_2_LMS_TO_LAB),
        "rgbVectors": rgb_vectors,
        "gamutProjectionVectors": projection_vectors,
    }


def build_oracle() -> dict:
    if colour.__version__ != COLOUR_SCIENCE_VERSION:
        raise RuntimeError(
            f"Expected colour-science {COLOUR_SCIENCE_VERSION}, got {colour.__version__}."
        )
    if np.__version__ != NUMPY_VERSION:
        raise RuntimeError(f"Expected NumPy {NUMPY_VERSION}, got {np.__version__}.")

    observer = colour.CCS_ILLUMINANTS["CIE 1931 2 Degree Standard Observer"]
    d50 = observer["ICC D50"]
    d65 = observer["D65"]
    return {
        "schemaVersion": 1,
        "generator": {
            "script": "scripts/generate-color-science-oracle.py",
            "colourScienceVersion": colour.__version__,
            "numpyVersion": np.__version__,
        },
        "sources": {
            "srgb": "IEC 61966-2-1:1999",
            "rec2020": "ITU-R BT.2020-2",
            "romm": "ISO 22028-2:2013",
            "chromaticAdaptation": "Bradford (Lam, 1985)",
            "colorChecker": "X-Rite ColorChecker Classic data published in 2016, pre-November-2014 edition",
            "cameraCharacterization": "Synthetic row-normalized camera matrix composed through IEC 61966-2-1 and ITU-R BT.2020",
            "oklab": "Björn Ottosson (2020), generated independently through colour-science XYZ_to_Oklab",
        },
        "spaces": [
            colour_space("linear-srgb-d65", "sRGB"),
            colour_space("linear-rec2020-d65", "ITU-R BT.2020"),
            colour_space("linear-romm-d50", "ROMM RGB"),
        ],
        "adaptations": [
            adaptation("bradford-d50-to-d65", d50, d65),
            adaptation("bradford-d65-to-d50", d65, d50),
        ],
        "cameraCharacterizations": [camera_characterization()],
        "oklab": oklab_vectors(),
        "transferFunctions": {
            "srgbEotf": [
                {"encoded": float(encoded), "linear": float(linear)}
                for encoded, linear in zip(
                    EOTF_SAMPLES,
                    colour.models.eotf_sRGB(EOTF_SAMPLES),
                    strict=True,
                )
            ]
        },
        "colorChecker": color_checker(),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "output",
        nargs="?",
        type=Path,
        default=Path("Tests/assets/color-science-oracle.json"),
    )
    args = parser.parse_args()
    args.output.write_text(
        json.dumps(build_oracle(), indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
