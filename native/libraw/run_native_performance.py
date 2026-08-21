#!/usr/bin/env python3
"""Run the isolated native candidate probe and enrich the managed report."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from prepare_macos_executable import prepare_macos_executable


CANDIDATE_LIBRARIES = {
    "win-x64": "raw_r.dll",
    "linux-x64": "libraw_r.so.25",
    "osx-arm64": "libraw.25.dylib",
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def run_set(executable: Path, runtime: Path, fixture: Path,
            libraw_name: str) -> list[dict]:
    measurements = []
    with tempfile.TemporaryDirectory(prefix="hplr-native-perf-") as temporary:
        isolated = Path(temporary)
        for source in runtime.iterdir():
            if source.is_file():
                shutil.copy2(source, isolated / source.name)
        local_executable = isolated / executable.name
        shutil.copy2(executable, local_executable)
        if sys.platform == "darwin":
            prepare_macos_executable(local_executable, libraw_name)
        for configuration in ("linear16-preview", "srgb8-full"):
            for sample in range(1, 4):
                result = subprocess.run(
                    [str(local_executable), str(fixture), configuration, str(sample)],
                    cwd=isolated, check=True, text=True, capture_output=True)
                measurements.append(json.loads(result.stdout))
    return measurements


def enrich(path: Path, metric: str, runtime: Path, probe: Path,
           measurements: list[dict]) -> None:
    report = json.loads(path.read_text(encoding="utf-8-sig"))
    report["NativeMemory"] = {
        "Metric": metric,
        "AcceptedGate": True,
        "RuntimeFile": runtime.name,
        "RuntimeSha256": sha256(runtime),
        "ProbeSha256": sha256(probe),
        "Measurements": measurements,
    }
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True, choices=CANDIDATE_LIBRARIES)
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--candidate-runtime", required=True, type=Path)
    parser.add_argument("--candidate-probe", required=True, type=Path)
    parser.add_argument("--candidate-report", required=True, type=Path)
    args = parser.parse_args()
    candidate_name = CANDIDATE_LIBRARIES[args.rid]
    candidate_library = args.candidate_runtime / candidate_name
    if not candidate_library.is_file():
        raise RuntimeError(f"candidate LibRaw runtime is absent: {candidate_library}")
    candidate = run_set(args.candidate_probe.resolve(), args.candidate_runtime.resolve(),
                        args.fixture.resolve(), candidate_name)
    metric = "peak-private-commit" if args.rid == "win-x64" else "peak-resident-set"
    enrich(args.candidate_report, metric, candidate_library, args.candidate_probe,
           candidate)


if __name__ == "__main__":
    main()
