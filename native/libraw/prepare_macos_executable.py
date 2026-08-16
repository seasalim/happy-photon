#!/usr/bin/env python3
"""Prepare a copied Mach-O executable for package-local LibRaw loading."""

from __future__ import annotations

import argparse
import re
import subprocess
from pathlib import Path


MACOS_LIBRAW_PATTERN = re.compile(r"^libraw\.\d+(?:\.\d+)*\.dylib$")


def run(*command: str) -> str:
    result = subprocess.run(command, check=True, text=True, capture_output=True)
    return result.stdout


def macho_dependencies(path: Path) -> list[str]:
    lines = run("otool", "-L", str(path)).splitlines()[1:]
    return [line.strip().split(" (compatibility", 1)[0] for line in lines]


def is_libraw(dependency: str) -> bool:
    return MACOS_LIBRAW_PATTERN.fullmatch(Path(dependency).name) is not None


def prepare_macos_executable(executable: Path, canonical_libraw: str) -> None:
    canonical_reference = f"@loader_path/{canonical_libraw}"
    for dependency in macho_dependencies(executable):
        if is_libraw(dependency) and dependency != canonical_reference:
            run("install_name_tool", "-change", dependency,
                canonical_reference, str(executable))
    run("codesign", "--force", "-s", "-", str(executable))
    run("codesign", "--verify", "--strict", str(executable))
    aliases = [dependency for dependency in macho_dependencies(executable)
               if is_libraw(dependency) and dependency != canonical_reference]
    if aliases:
        raise RuntimeError(
            f"executable retains non-canonical LibRaw references: {aliases}")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--canonical-libraw", required=True)
    parser.add_argument("--executable", required=True, action="append", type=Path)
    args = parser.parse_args()
    for executable in args.executable:
        prepare_macos_executable(executable.resolve(), args.canonical_libraw)


if __name__ == "__main__":
    main()
