#!/usr/bin/env python3
"""Validate a staged LibRaw/bridge runtime against the Checkpoint B contract."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
from pathlib import Path


EXPECTED = {
    "win-x64": ("happyphoton_libraw_bridge.dll", "raw_r.dll", True),
    "linux-x64": ("libhappyphoton_libraw_bridge.so", "libraw_r.so.25", True),
    "osx-arm64": ("libhappyphoton_libraw_bridge.dylib", "libraw.25.dylib", False),
}
ZLIB_CAPABILITY = 1 << 6
JPEG_CAPABILITY = 1 << 7
_DUMPBIN: str | None = None


def run(*command: str, cwd: Path | None = None,
        environment: dict[str, str] | None = None) -> str:
    result = subprocess.run(command, cwd=cwd, env=environment, check=True,
                            text=True, capture_output=True)
    return result.stdout


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def validate_camera_facts(probe: dict[str, object],
                          feature: dict[str, object]) -> dict[str, object]:
    bridge = probe.get("camera_facts")
    direct = feature.get("camera_facts")
    require(isinstance(bridge, dict), "bridge camera facts are missing")
    require(isinstance(direct, dict), "direct LibRaw camera facts are missing")
    pre_mul_count = direct.get("pre_mul_count")
    require(pre_mul_count in (0, 3, 4), "direct pre_mul channel count is invalid")
    require(len(direct.get("pre_mul", [])) == pre_mul_count,
            "direct pre_mul value count is invalid")
    camera_from_xyz_rows = direct.get("camera_from_xyz_rows")
    camera_from_xyz_columns = direct.get("camera_from_xyz_columns")
    require(camera_from_xyz_rows in (0, 3, 4) and
            camera_from_xyz_columns == (3 if camera_from_xyz_rows else 0),
            "direct camera-from-XYZ dimensions are invalid")
    require(len(direct.get("camera_from_xyz", [])) ==
            camera_from_xyz_rows * camera_from_xyz_columns,
            "direct camera-from-XYZ value count is invalid")
    linear_max_count = direct.get("linear_max_count")
    require(linear_max_count in (0, 3, 4) and
            len(direct.get("linear_max", [])) == linear_max_count,
            "direct linear_max value count is invalid")
    require(bridge == direct,
            "bridge camera fact counts, ordering, or values differ from direct LibRaw")
    return bridge


def find_dumpbin() -> str:
    override = os.environ.get("HPLR_DUMPBIN")
    if override:
        path = Path(override).resolve()
        require(path.is_file(), f"HPLR_DUMPBIN does not name a file: {path}")
        return str(path)
    available = shutil.which("dumpbin")
    if available:
        return available
    installer = Path(os.environ.get("ProgramFiles(x86)", "")) / (
        "Microsoft Visual Studio/Installer/vswhere.exe")
    if installer.is_file():
        installation = run(str(installer), "-latest", "-products", "*", "-requires",
                           "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
                           "-property", "installationPath").strip()
        if installation:
            tools = Path(installation) / "VC/Tools/MSVC"
            matches = sorted(
                tools.glob("*/bin/Hostx64/x64/dumpbin.exe"),
                key=lambda path: tuple(int(part) for part in path.parents[3].name.split(".")),
                reverse=True)
            if matches:
                return str(matches[0])
    raise RuntimeError(
        "dumpbin.exe was not found. Install the Visual C++ x64 tools, run from a "
        "VS developer shell, or set HPLR_DUMPBIN to its full path.")


def dumpbin(*arguments: str) -> str:
    global _DUMPBIN
    if _DUMPBIN is None:
        _DUMPBIN = find_dumpbin()
    return run(_DUMPBIN, "/nologo", *arguments)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def elf_dynamic(path: Path, field: str) -> list[str]:
    output = run("readelf", "-d", str(path))
    return re.findall(rf"\({field}\).*?\[([^]]+)]", output)


def macho_dependencies(path: Path) -> list[str]:
    return [line.strip().split(" (compatibility", 1)[0]
            for line in run("otool", "-L", str(path)).splitlines()[1:]]


def macho_identity(path: Path) -> str:
    identities = [line.strip() for line in run("otool", "-D", str(path)).splitlines()[1:]]
    require(len(identities) == 1, f"{path.name} has an invalid Mach-O identity: {identities}")
    return identities[0]


def pe_dependencies(path: Path) -> list[str]:
    output = dumpbin("/dependents", str(path))
    return sorted(set(re.findall(r"(?im)^\s+([\w.+-]+\.dll)\s*$", output)))


def symbol_set(command: tuple[str, ...]) -> set[str]:
    output = run(*command)
    symbols: set[str] = set()
    for line in output.splitlines():
        token = line.split()[-1] if line.split() else ""
        if "LibRaw" in token or "libraw_" in token:
            symbols.add(token.split("@", 1)[0])
    return symbols


def pe_symbols(output: str, *, exports: bool = False) -> set[str]:
    symbols: set[str] = set()
    for line in output.splitlines():
        undecorated = line.split(" = ", 1)[0]
        if exports:
            match = re.match(
                r"^\s*\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+(\S+)", undecorated)
            token = match.group(1) if match else ""
        else:
            token = undecorated.split()[-1] if undecorated.split() else ""
        if ("LibRaw" in token or "libraw_" in token) and not any(
                marker in token for marker in ("\\", "/", ":")) and not token.lower().endswith(".dll"):
            symbols.add(token)
    return symbols


def validate_symbols(rid: str, bridge: Path, raw: Path) -> dict[str, object]:
    if rid == "win-x64":
        imports_text = dumpbin("/imports:raw_r.dll", str(bridge))
        exports_text = dumpbin("/exports", str(raw))
        imports = pe_symbols(imports_text)
        exports = pe_symbols(exports_text, exports=True)
        bridge_exports = dumpbin("/exports", str(bridge))
        cpp_imports = {name for name in imports if name.startswith("?")}
        cpp_exports = {name for name in exports if name.startswith("?")}
        require(not cpp_imports or bool(cpp_exports),
                "bridge imports C++ LibRaw symbols but its export table contained none")
    elif rid == "linux-x64":
        imports = symbol_set(("nm", "-D", "--undefined-only", str(bridge)))
        exports = symbol_set(("nm", "-D", "--defined-only", str(raw)))
        bridge_exports = run("nm", "-D", "--defined-only", str(bridge))
    else:
        imports = symbol_set(("nm", "-u", "-j", str(bridge)))
        exports = symbol_set(("nm", "-gU", "-j", str(raw)))
        bridge_exports = run("nm", "-gU", "-j", str(bridge))
    unresolved = sorted(imports - exports)
    require(bool(imports), "bridge inspection found no LibRaw imports")
    require(not unresolved, f"bridge imports absent from LibRaw exports: {unresolved}")
    require("hplr_test_" not in bridge_exports,
            "production bridge exposes test-only fault injection hooks")
    return {"bridge_libraw_import_count": len(imports),
            "libraw_export_count": len(exports), "unresolved": unresolved}


def validate_windows(runtime: Path, bridge: Path, raw: Path) -> dict[str, object]:
    bridge_headers = dumpbin("/headers", str(bridge))
    raw_headers = dumpbin("/headers", str(raw))
    require("8664 machine (x64)" in bridge_headers.lower(), "bridge is not PE x64")
    require("8664 machine (x64)" in raw_headers.lower(), "LibRaw is not PE x64")
    bridge_deps = pe_dependencies(bridge)
    raw_deps = pe_dependencies(raw)
    require(any(name.lower() == "raw_r.dll" for name in bridge_deps),
            "bridge does not import raw_r.dll")
    require(any("lcms2" in name.lower() for name in raw_deps), "LCMS dependency is absent")
    require(any("vcomp" in name.lower() for name in raw_deps), "VCOMP OpenMP is absent")
    require(not (runtime / "raw.dll").exists(), "non-reentrant raw.dll was staged")
    versions = re.findall(r"(?im)^\s*([0-9.]+) subsystem version", bridge_headers)
    return {"bridge_dependencies": bridge_deps, "libraw_dependencies": raw_deps,
            "minimum_os": versions[0] if versions else "PE subsystem header"}


def validate_linux(runtime: Path, bridge: Path, raw: Path) -> dict[str, object]:
    headers = {path.name: run("readelf", "-h", str(path)) for path in (bridge, raw)}
    require(all("Advanced Micro Devices X86-64" in value for value in headers.values()),
            "ELF candidate is not x86-64")
    bridge_needed = elf_dynamic(bridge, "NEEDED")
    raw_needed = elf_dynamic(raw, "NEEDED")
    require("libraw_r.so.25" in bridge_needed, "bridge NEEDED identity is not ABI 25")
    require(elf_dynamic(raw, "SONAME") == ["libraw_r.so.25"], "LibRaw SONAME mismatch")
    require("$ORIGIN" in elf_dynamic(bridge, "RUNPATH") + elf_dynamic(bridge, "RPATH"),
            "bridge is missing package-local $ORIGIN resolution")
    require(any("lcms2" in name for name in raw_needed), "LCMS dependency is absent")
    require(any("libgomp" in name for name in raw_needed), "libgomp OpenMP is absent")
    environment = dict(os.environ)
    environment.pop("LD_LIBRARY_PATH", None)
    ldd = run("ldd", str(bridge), cwd=runtime, environment=environment)
    require("not found" not in ldd, "ELF dependency resolution contains an unresolved import")
    raw_line = next((line for line in ldd.splitlines() if "libraw_r.so.25 =>" in line), "")
    require(str(runtime.resolve()) in raw_line, "bridge did not resolve package-local LibRaw")
    versions: set[tuple[int, int]] = set()
    for path in runtime.iterdir():
        text = run("readelf", "--version-info", str(path))
        versions.update((int(a), int(b)) for a, b in re.findall(r"GLIBC_(\d+)\.(\d+)", text))
    ceiling = max(versions, default=(0, 0))
    require(ceiling <= (2, 35), f"GLIBC symbol ceiling {ceiling} exceeds Ubuntu 22.04")
    return {"bridge_dependencies": bridge_needed, "libraw_dependencies": raw_needed,
            "resolution": ldd.splitlines(), "glibc_symbol_ceiling": ".".join(map(str, ceiling))}


def validate_macos(runtime: Path, bridge: Path, raw: Path) -> dict[str, object]:
    for path in (bridge, raw):
        identity = run("file", str(path))
        require("arm64" in identity and "x86_64" not in identity,
                f"{path.name} is not arm64-only")
    bridge_deps = macho_dependencies(bridge)
    raw_deps = macho_dependencies(raw)
    bridge_identity = f"@loader_path/{bridge.name}"
    raw_identity = "@loader_path/libraw.25.dylib"
    require(macho_identity(bridge) == bridge_identity, "bridge install name mismatch")
    require(macho_identity(raw) == raw_identity, "LibRaw install name mismatch")
    is_system = lambda name: name.startswith(("/usr/lib/", "/System/Library/"))
    bridge_local = [name for name in bridge_deps
                    if name != bridge_identity and not is_system(name)]
    raw_local = [name for name in raw_deps
                 if name != raw_identity and not is_system(name)]
    require(bridge_local == [raw_identity],
            f"bridge package-local dependency allowlist mismatch: {bridge_local}")
    require(not raw_local, f"LibRaw has unapproved loose dependencies: {raw_local}")
    for path in (bridge, raw):
        run("codesign", "--verify", "--strict", str(path))
    forbidden = [name for name in raw_deps + bridge_deps
                 if any(part in name.lower() for part in ("lcms", "libomp", "libgomp", "jpeg"))]
    require(not forbidden, f"forbidden macOS dependencies: {forbidden}")
    require(any(Path(name).name.startswith("libz.") for name in raw_deps),
            "macOS LibRaw does not use system zlib")
    load = run("otool", "-l", str(raw))
    minos = re.findall(r"\bminos\s+([0-9.]+)", load)
    require(minos and tuple(map(int, minos[0].split("."))) <= (13, 0),
            "macOS deployment target exceeds 13.0")
    loose = [path.name for path in runtime.iterdir() if path.suffix == ".dylib"]
    require(sorted(loose) == sorted([bridge.name, raw.name]),
            f"unexpected loose macOS dylibs: {loose}")
    return {"bridge_dependencies": bridge_deps, "libraw_dependencies": raw_deps,
            "bridge_identity": bridge_identity, "libraw_identity": raw_identity,
            "minimum_os": minos[0]}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--rid", required=True, choices=EXPECTED)
    parser.add_argument("--runtime-dir", required=True, type=Path)
    parser.add_argument("--runtime-probe", required=True, type=Path)
    parser.add_argument("--feature-probe", required=True, type=Path)
    parser.add_argument("--thread-comparison", required=True, type=Path)
    parser.add_argument("--build-options", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    runtime = args.runtime_dir.resolve()
    bridge_name, raw_name, thread_safe = EXPECTED[args.rid]
    bridge, raw = runtime / bridge_name, runtime / raw_name
    require(bridge.is_file() and raw.is_file(), "candidate bridge or LibRaw is missing")
    names = [path.name for path in runtime.iterdir()]
    require(not any(".23" in name for name in names), "ABI-23 alias is present")
    require(not any(name.lower().endswith((".pdb", ".dSYM".lower())) for name in names),
            "debug artifact is present in Release runtime")
    probe = json.loads(args.runtime_probe.read_text(encoding="utf-8"))
    feature = json.loads(args.feature_probe.read_text(encoding="utf-8"))
    threading = json.loads(args.thread_comparison.read_text(encoding="utf-8"))
    options = json.loads(args.build_options.read_text(encoding="utf-8"))
    require(probe["bridge_abi"] == 4, "bridge ABI is not 4")
    require(probe["libraw_version"] == 0x001602, "LibRaw version is not 0.22.2")
    require(probe["capabilities"] & JPEG_CAPABILITY, "JPEG capability bit is absent")
    require(probe["capabilities"] & ZLIB_CAPABILITY, "zlib capability bit is absent")
    require(probe["thread_safe"] is thread_safe, "threading variant does not match RID")
    require(options.get("configuration") == "Release", "candidate is not a Release build")
    require(feature["lcms"] is (args.rid != "osx-arm64"), "LCMS functional probe mismatch")
    require(feature["openmp"] is (args.rid != "osx-arm64"), "OpenMP probe mismatch")
    require(isinstance(probe.get("default_checksum"), str) and
            bool(probe["default_checksum"]),
            "bridge default-configuration checksum is missing")
    require(feature.get("output_defaults") == {
        "user_sat": -1,
        "user_qual": -1,
        "cropbox": [0, 0, 4294967295, 4294967295],
    }, "LibRaw output defaults do not match the output-configuration absence contract")
    require(threading["enabled"] is (args.rid != "osx-arm64"),
            "OpenMP comparison enablement mismatch")
    if threading["enabled"]:
        require(threading["constrained"]["checksum"] == threading["parallel"]["checksum"],
                "OpenMP constrained and parallel output checksums differ")
    camera_facts = validate_camera_facts(probe, feature)
    platform_report = {"win-x64": validate_windows, "linux-x64": validate_linux,
                       "osx-arm64": validate_macos}[args.rid](runtime, bridge, raw)
    report = {"rid": args.rid, "runtime": probe, "feature_probe": feature,
              "camera_facts": camera_facts,
              "thread_comparison": threading,
              "build_options": options, "files": [
                  {"name": path.name, "sha256": sha256(path), "bytes": path.stat().st_size}
                  for path in sorted(runtime.iterdir())],
              "symbols": validate_symbols(args.rid, bridge, raw), **platform_report}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
