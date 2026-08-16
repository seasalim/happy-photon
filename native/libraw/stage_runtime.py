#!/usr/bin/env python3
"""Stage the minimal package-local native dependency closure for one RID."""

from __future__ import annotations

import argparse
import json
import re
import shutil
import subprocess
from pathlib import Path


MACOS_LIBRAW_NAME = "libraw.25.dylib"
MACOS_LIBRAW_PATTERN = re.compile(r"^libraw\.25(?:\.\d+)*\.dylib$")


def run(*command: str) -> str:
    result = subprocess.run(command, check=True, text=True, capture_output=True)
    return result.stdout


def one(paths: list[Path], description: str) -> Path:
    files = [path for path in paths if path.is_file() and "_test" not in path.name]
    if len(files) != 1:
        raise RuntimeError(f"expected one {description}, found: {files}")
    return files[0]


def copy(source: Path, destination: Path) -> Path:
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source.resolve(), destination)
    return destination


def pe_dependencies(path: Path) -> list[str]:
    output = run("dumpbin", "/nologo", "/dependents", str(path))
    return sorted(set(re.findall(r"(?im)^\s+([\w.+-]+\.dll)\s*$", output)))


def elf_dynamic(path: Path, field: str) -> list[str]:
    output = run("readelf", "-d", str(path))
    return re.findall(rf"\({field}\).*?\[([^]]+)]", output)


def macho_dependencies(path: Path) -> list[str]:
    lines = run("otool", "-L", str(path)).splitlines()[1:]
    return [line.strip().split(" (compatibility", 1)[0] for line in lines]


def is_macos_libraw(dependency: str) -> bool:
    return MACOS_LIBRAW_PATTERN.fullmatch(Path(dependency).name) is not None


def candidate(root: Path, name: str) -> Path | None:
    matches = [path for path in root.rglob(name) if path.is_file()]
    return matches[0] if matches else None


def stage_windows(build: Path, installed: Path, output: Path) -> list[str]:
    bridge = one(list(build.rglob("happyphoton_libraw_bridge.dll")), "bridge DLL")
    raw = one(list(installed.rglob("raw_r.dll")), "reentrant LibRaw DLL")
    queue = [copy(bridge, output / bridge.name), copy(raw, output / raw.name)]
    external: set[str] = set()
    seen: set[str] = set()
    while queue:
        current = queue.pop()
        for dependency in pe_dependencies(current):
            key = dependency.lower()
            if key in seen:
                continue
            seen.add(key)
            source = candidate(installed / "bin", dependency)
            if source:
                queue.append(copy(source, output / dependency))
            else:
                external.add(dependency)
    return sorted(external, key=str.lower)


def stage_linux(build: Path, installed: Path, output: Path) -> list[str]:
    bridge = one(list(build.rglob("libhappyphoton_libraw_bridge.so")), "bridge SO")
    raw = one(list(installed.rglob("libraw_r.so.25")), "reentrant LibRaw SO")
    queue = [copy(bridge, output / bridge.name), copy(raw, output / "libraw_r.so.25")]
    external: set[str] = set()
    seen = {path.name for path in queue}
    while queue:
        current = queue.pop()
        for dependency in elf_dynamic(current, "NEEDED"):
            if dependency in seen:
                continue
            seen.add(dependency)
            source = candidate(installed, dependency)
            if source:
                staged = copy(source, output / dependency)
                queue.append(staged)
            else:
                external.add(dependency)
    for binary in output.iterdir():
        run("patchelf", "--set-rpath", "$ORIGIN", str(binary))
    run("patchelf", "--set-soname", "libraw_r.so.25", str(output / "libraw_r.so.25"))
    run("patchelf", "--set-soname", bridge.name, str(output / bridge.name))
    return sorted(external)


def stage_macos(build: Path, installed: Path, output: Path) -> list[str]:
    bridge = one(list(build.rglob("libhappyphoton_libraw_bridge.dylib")), "bridge dylib")
    raw = one(list(installed.rglob(MACOS_LIBRAW_NAME)), "non-reentrant LibRaw dylib")
    staged_bridge = copy(bridge, output / bridge.name)
    staged_raw = copy(raw, output / MACOS_LIBRAW_NAME)
    external: set[str] = set()
    for binary in (staged_bridge, staged_raw):
        for dependency in macho_dependencies(binary):
            name = Path(dependency).name
            if binary == staged_bridge and is_macos_libraw(dependency):
                run("install_name_tool", "-change", dependency,
                    f"@loader_path/{staged_raw.name}", str(binary))
            elif binary == staged_raw and is_macos_libraw(dependency):
                continue
            elif dependency.startswith(("/usr/lib/", "/System/Library/")):
                external.add(dependency)
            elif name != binary.name:
                raise RuntimeError(f"unapproved loose macOS dependency: {dependency}")
    run("install_name_tool", "-id", f"@loader_path/{staged_bridge.name}", str(staged_bridge))
    run("install_name_tool", "-id", f"@loader_path/{staged_raw.name}", str(staged_raw))
    for binary in (staged_raw, staged_bridge):
        run("codesign", "--force", "-s", "-", str(binary))
        run("codesign", "--verify", "--strict", str(binary))
    bridge_dependencies = macho_dependencies(staged_bridge)
    if f"@loader_path/{staged_raw.name}" not in bridge_dependencies:
        raise RuntimeError("staged bridge does not reference canonical package-local LibRaw")
    aliases = [dependency for dependency in bridge_dependencies
               if is_macos_libraw(dependency)
               and dependency != f"@loader_path/{staged_raw.name}"]
    if aliases:
        raise RuntimeError(f"staged bridge retains non-canonical LibRaw references: {aliases}")
    return sorted(external)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True,
                        choices=("win-x64", "linux-x64", "osx-arm64"))
    parser.add_argument("--build-dir", required=True, type=Path)
    parser.add_argument("--installed-dir", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    if args.output.exists() and any(args.output.iterdir()):
        raise RuntimeError(f"runtime staging directory is not empty: {args.output}")
    args.output.mkdir(parents=True, exist_ok=True)
    stage = {
        "win-x64": stage_windows,
        "linux-x64": stage_linux,
        "osx-arm64": stage_macos,
    }[args.rid]
    external = stage(args.build_dir.resolve(), args.installed_dir.resolve(),
                     args.output.resolve())
    aliases = [path.name for path in args.output.iterdir() if ".23" in path.name]
    if aliases:
        raise RuntimeError(f"ABI-23 aliases are forbidden: {aliases}")
    inventory = {
        "rid": args.rid,
        "files": sorted(path.name for path in args.output.iterdir()),
        "external_prerequisites": external,
    }
    (args.output.parent / "staging-inventory.json").write_text(
        json.dumps(inventory, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
