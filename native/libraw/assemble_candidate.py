#!/usr/bin/env python3
"""Assemble a reviewed multi-RID native NuGet candidate without committing it."""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import zipfile
from datetime import datetime, timezone
from pathlib import Path


RIDS = ("win-x64", "linux-x64", "osx-arm64")
EXPECTED = {
    "win-x64": ("happyphoton_libraw_bridge.dll", "raw_r.dll"),
    "linux-x64": ("libhappyphoton_libraw_bridge.so", "libraw_r.so.25"),
    "osx-arm64": ("libhappyphoton_libraw_bridge.dylib", "libraw.25.dylib"),
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def parse_artifacts(values: list[str]) -> dict[str, Path]:
    artifacts: dict[str, Path] = {}
    for value in values:
        rid, separator, path = value.partition("=")
        if not separator or rid not in RIDS or rid in artifacts:
            raise RuntimeError(f"expected one RID=PATH argument for each RID: {value}")
        artifacts[rid] = Path(path).resolve()
    if set(artifacts) != set(RIDS):
        raise RuntimeError(f"artifact set must contain {RIDS}")
    return artifacts


def check_version(version: str) -> None:
    if not re.fullmatch(r"0\.22\.2\.[1-9][0-9]*", version):
        raise RuntimeError("candidate version must be 0.22.2.N with N greater than zero")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True)
    parser.add_argument("--rid-artifact", action="append", required=True)
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    check_version(args.version)
    artifacts = parse_artifacts(args.rid_artifact)
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    package = output / f"HappyPhoton.LibRaw.Native.{args.version}.nupkg"
    if package.exists():
        raise RuntimeError(f"candidate version already exists: {package}")
    provenance = []
    for rid, root in artifacts.items():
        report = json.loads((root / "provenance.json").read_text(encoding="utf-8"))
        if report["package"]["version"] != args.version or report["rid"] != rid:
            raise RuntimeError(f"provenance identity mismatch for {rid}")
        runtime = root / "runtime"
        names = {path.name for path in runtime.iterdir()}
        if not set(EXPECTED[rid]).issubset(names):
            raise RuntimeError(f"candidate bridge or LibRaw is missing for {rid}")
        if any(".23" in name for name in names):
            raise RuntimeError(f"ABI-23 alias present for {rid}")
        validation = root / "validation/validation-report.json"
        if not validation.is_file() or not (root / "licenses").is_dir():
            raise RuntimeError(f"validation or licenses missing for {rid}")
        if report["validation_report_sha256"] != sha256(validation):
            raise RuntimeError(f"validation report hash mismatch for {rid}")
        provenance.append(report)
    commits = {report["repository_commit"] for report in provenance}
    if len(commits) != 1:
        raise RuntimeError(f"per-RID source commits differ: {sorted(commits)}")
    combined = {
        "schema": 1,
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "package": {"id": "HappyPhoton.LibRaw.Native", "version": args.version},
        "rids": provenance,
    }
    inventory = output / "native-provenance.json"
    inventory.write_text(json.dumps(combined, indent=2) + "\n", encoding="utf-8")
    nuspec = f"""<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>HappyPhoton.LibRaw.Native</id>
    <version>{args.version}</version>
    <authors>Happy Photon contributors</authors>
    <description>Pinned Happy Photon LibRaw 0.22.2 native runtime candidate.</description>
    <license type="expression">GPL-3.0-or-later</license>
    <requireLicenseAcceptance>false</requireLicenseAcceptance>
  </metadata>
</package>
"""
    with zipfile.ZipFile(package, "x", compression=zipfile.ZIP_DEFLATED,
                         compresslevel=9) as archive:
        archive.writestr("HappyPhoton.LibRaw.Native.nuspec", nuspec)
        archive.write(inventory, "build/native-provenance.json")
        for rid, root in artifacts.items():
            for path in sorted((root / "runtime").iterdir()):
                archive.write(path, f"runtimes/{rid}/native/{path.name}")
            for path in sorted((root / "licenses").iterdir()):
                archive.write(path, f"licenses/{rid}/{path.name}")
    summary = {"package": package.name, "sha256": sha256(package),
               "provenance": inventory.name, "provenance_sha256": sha256(inventory)}
    (output / "candidate-summary.json").write_text(
        json.dumps(summary, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
