#!/usr/bin/env python3
"""Collect source, toolchain, dependency, license, and output provenance."""

from __future__ import annotations

import argparse
import hashlib
import json
import platform
import re
import shutil
import subprocess
from datetime import datetime, timezone
from pathlib import Path


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def version(*command: str) -> str:
    try:
        result = subprocess.run(command, text=True, capture_output=True, timeout=20)
        return (result.stdout or result.stderr).strip()
    except (OSError, subprocess.TimeoutExpired) as error:
        return f"unavailable: {error}"


def package_records(installed: Path) -> list[dict[str, object]]:
    source_licenses = {
        "LibRaw/LibRaw": "LGPL-2.1-only OR CDDL-1.0",
        "LibRaw/LibRaw-cmake": "BSD-3-Clause",
        "libjpeg-turbo/libjpeg-turbo": "BSD-3-Clause AND IJG AND Zlib",
        "madler/zlib": "Zlib",
        "mm2/Little-CMS": "MIT",
        "jasper-software/jasper": "JasPer-2.0",
    }
    records: dict[tuple[str, str], dict[str, object]] = {}
    for path in sorted((installed / "share").glob("*/vcpkg.spdx.json")):
        document = json.loads(path.read_text(encoding="utf-8"))
        for package in document.get("packages", []):
            name = package.get("name", "unknown")
            revision = package.get("versionInfo", "unknown")
            key = (name, revision)
            sources = []
            for reference in package.get("externalRefs", []):
                sources.append({"type": reference.get("referenceType"),
                                "locator": reference.get("referenceLocator")})
            concluded = package.get("licenseConcluded")
            license_name = (package.get("licenseDeclared", "NOASSERTION")
                            if concluded in (None, "NOASSERTION") else concluded)
            if license_name == "NOASSERTION":
                license_name = source_licenses.get(name, license_name)
            if license_name == "NOASSERTION" and name.startswith("meson-"):
                license_name = "Apache-2.0"
            records[key] = {
                "name": name,
                "version": revision,
                "download_location": package.get("downloadLocation", "NOASSERTION"),
                "license": license_name,
                "checksums": package.get("checksums", []),
                "source_references": sources,
                "sbom_sha256": sha256(path),
            }
    return [records[key] for key in sorted(records)]


def external_records(rid: str, names: list[str]) -> list[dict[str, str]]:
    records = []
    for name in names:
        lowered = name.lower()
        if "libgomp" in lowered or "libstdc++" in lowered or "libgcc" in lowered:
            license_name = "GPL-3.0-or-later WITH GCC-exception-3.1"
            source = "Ubuntu 22.04 runner GCC runtime"
        elif "libc.so" in lowered or "libm.so" in lowered or "ld-linux" in lowered:
            license_name = "LGPL-2.1-or-later"
            source = "Ubuntu 22.04 runner glibc"
        elif "libz" in lowered and rid == "osx-arm64":
            license_name = "Zlib"
            source = "macOS 13 system library"
        elif rid == "osx-arm64":
            license_name = "Apple SDK and operating-system terms"
            source = "macOS 13 system library"
        elif rid == "win-x64":
            license_name = "Microsoft Windows or Visual C++ runtime terms"
            source = "Windows runner prerequisite"
        else:
            license_name = "Linux kernel or system runtime terms"
            source = "Ubuntu 22.04 runner prerequisite"
        records.append({"name": name, "source": source, "license": license_name})
    return records


def copy_licenses(installed: Path, artifact: Path) -> list[dict[str, str]]:
    destination = artifact / "licenses"
    destination.mkdir(exist_ok=True)
    copied: list[dict[str, str]] = []
    for source in sorted((installed / "share").glob("*/copyright")):
        target = destination / f"{source.parent.name}-copyright.txt"
        shutil.copy2(source, target)
        copied.append({"package": source.parent.name, "file": target.name,
                       "sha256": sha256(target)})
    project_license = Path(__file__).resolve().parents[2] / "LICENSE"
    target = destination / "HappyPhoton-GPL-3.0-or-later.txt"
    shutil.copy2(project_license, target)
    copied.append({"package": "HappyPhoton bridge", "file": target.name,
                   "sha256": sha256(target)})
    return copied


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True)
    parser.add_argument("--package-version", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--vcpkg-root", required=True, type=Path)
    parser.add_argument("--installed-dir", required=True, type=Path)
    parser.add_argument("--artifact-dir", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    require_version = re.fullmatch(r"0\.22\.2\.(0|[1-9][0-9]*)", args.package_version)
    if not require_version:
        raise RuntimeError("package version must be 0.22.2.0 or a unique 0.22.2.N revision")
    developer_build = args.package_version == "0.22.2.0"
    artifact = args.artifact_dir.resolve()
    installed = args.installed_dir.resolve()
    validation = json.loads((artifact / "validation/validation-report.json").read_text())
    staging = json.loads((artifact / "staging-inventory.json").read_text())
    options = json.loads((artifact / "build-options.json").read_text())
    licenses = copy_licenses(installed, artifact)
    report = {
        "schema": 1,
        "created_utc": datetime.now(timezone.utc).isoformat(),
        "rid": args.rid,
        "package": {"id": "HappyPhoton.LibRaw.Native",
                    "version": args.package_version,
                    "candidate": not developer_build,
                    "build_revision_source": (
                        "developer-local-reserved-zero" if developer_build
                        else "github.run_number")},
        "repository_commit": args.source_commit,
        "vcpkg": {"revision": options["vcpkg_revision"],
                  "checkout": str(args.vcpkg_root.resolve())},
        "sources_and_dependencies": package_records(installed),
        "build_options": options,
        "toolchain": {
            "os": platform.platform(),
            "python": platform.python_version(),
            "cmake": version("cmake", "--version"),
            "ninja": version("ninja", "--version"),
            "compiler": version("cl") if platform.system() == "Windows"
                        else version("c++", "--version"),
        },
        "outputs": validation["files"],
        "bundled_dependencies": staging["files"],
        "external_prerequisites": external_records(
            args.rid, staging["external_prerequisites"]),
        "licenses": licenses,
        "validation_report_sha256": sha256(artifact / "validation/validation-report.json"),
    }
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
