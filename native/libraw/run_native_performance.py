#!/usr/bin/env python3
"""Run isolated native baseline/candidate probes and enrich managed reports."""

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


BASELINE_HASHES = {
    "win-x64": ("raw_r.dll", "F500C0732FEB21B188D5B52CEA05FD824D5B3C8016EB2CA68D8312ACC9F914B9"),
    "linux-x64": ("libraw_r.so.23", "F42039E9865385F64B708182B5ACA59D39FEB0608467E666103788D3B782E042"),
    "osx-arm64": ("libraw.23.dylib", "F9A2CA9CEBD3DDBF134123F8DAB0A0A3B67D4CBE44B459346D31FC089B4F89B6"),
}
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
    parser.add_argument("--rid", required=True, choices=BASELINE_HASHES)
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--baseline-runtime", required=True, type=Path)
    parser.add_argument("--candidate-runtime", required=True, type=Path)
    parser.add_argument("--baseline-probe", required=True, type=Path)
    parser.add_argument("--candidate-probe", required=True, type=Path)
    parser.add_argument("--baseline-report", required=True, type=Path)
    parser.add_argument("--candidate-report", required=True, type=Path)
    args = parser.parse_args()
    name, expected_hash = BASELINE_HASHES[args.rid]
    baseline_library = args.baseline_runtime / name
    if not baseline_library.is_file() or sha256(baseline_library) != expected_hash:
        raise RuntimeError(f"baseline runtime is not the audited {args.rid} LibRaw binary")
    baseline = run_set(args.baseline_probe.resolve(), args.baseline_runtime.resolve(),
                       args.fixture.resolve(), name)
    candidate_name = CANDIDATE_LIBRARIES[args.rid]
    candidate = run_set(args.candidate_probe.resolve(), args.candidate_runtime.resolve(),
                        args.fixture.resolve(), candidate_name)
    candidate_library = args.candidate_runtime / candidate_name
    if not candidate_library.is_file():
        raise RuntimeError(f"candidate LibRaw runtime is absent: {candidate_library}")
    metric = "peak-private-commit" if args.rid == "win-x64" else "peak-resident-set"
    enrich(args.baseline_report, metric, baseline_library, args.baseline_probe, baseline)
    enrich(args.candidate_report, metric, candidate_library, args.candidate_probe, candidate)


if __name__ == "__main__":
    main()
