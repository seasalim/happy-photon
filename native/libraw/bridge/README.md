# Happy Photon LibRaw bridge

This directory defines bridge ABI version 3. All ABI values use fixed-width
integers and the public header is consumable as C. Inputs described as UTF-8
are pointer-plus-length values: embedded NUL and malformed UTF-8 are rejected.
Fixed textual outputs contain a byte length and replace invalid native bytes
with U+FFFD.

Handles, mosaic leases, and image allocations are monotonically issued tokens.
Unknown, stale, and already-released tokens are rejected. A mosaic borrow is
allocation-free and mutable; writes made before release are the mosaic consumed
by the next process call. Release it before process, recycle, or close. Those operations
return `HPLR_E_BUSY` immediately while a borrow or another handle operation is
active. Image allocations have exactly one matching `hplr_free_image` call.
No pointer returned by this bridge is valid after its owning token is released.

Output configuration is replacing rather than cumulative. ABI v3 adds an optional
post-black saturation ceiling, a named demosaic-quality request, and a full-resolution
pre-rotation crop. Zero-filled quality and crop presence flags preserve LibRaw's own
defaults. Accepted crops are limited to regions LibRaw uses verbatim; sensor-dependent
demosaic fallbacks remain LibRaw's behavior.

Errors are per-call caller-owned values. LibRaw failures preserve its numeric
code and text. ABI failures, programming/ownership failures, and bridge
resource/internal failures use distinct classes. `HPLR_ABSENT` (missing
metadata) and `HPLR_UNAVAILABLE` (a non-ushort mosaic layout) are non-errors.

Windows and Linux candidates select `libraw::raw_r`; macOS selects
`libraw::raw`. Non-reentrant builds serialize LibRaw calls globally; every
build rejects concurrent work on the same handle rather than waiting. When
tests are enabled, hook-dependent cases link a separate
`happyphoton_libraw_bridge_test` target. The production bridge never defines or
exports `HPLR_TESTING` hooks. Public-ABI smoke and decode cases link the exact
production candidate.

## Development build

Set `VCPKG_ROOT` to the pinned vcpkg checkout where the repository's LibRaw
overlay was installed, then configure, build, and test the Windows development
variant from the repository root:

```powershell
cmake -S native/libraw/bridge -B artifacts/libraw-bridge -G Ninja -DCMAKE_BUILD_TYPE=Release -DCMAKE_TOOLCHAIN_FILE="$env:VCPKG_ROOT/scripts/buildsystems/vcpkg.cmake" -DVCPKG_TARGET_TRIPLET=x64-windows
cmake --build artifacts/libraw-bridge
ctest --test-dir artifacts/libraw-bridge --output-on-failure
```

CTest supplies the bridge and LibRaw DLL search paths itself; no caller-provided
`PATH` changes are required. To exercise the managed integration test, make one
isolated directory containing `happyphoton_libraw_bridge.dll`, `raw_r.dll`, and
all of their runtime dependency DLLs. Point `HAPPY_PHOTON_LIBRAW_BRIDGE_DIR` at
that complete directory before starting the test host:

```powershell
$env:HAPPY_PHOTON_LIBRAW_BRIDGE_DIR = "C:\path\to\isolated\libraw-runtime"
dotnet test Interop/HappyPhoton.LibRaw.Interop.Tests/HappyPhoton.LibRaw.Interop.Tests.csproj --configuration Release
```

The integration test copies the DLL set into its own temporary staging
directory and verifies the loaded bridge and LibRaw paths and hashes before it
creates a native handle. Without the environment variable, the native-runtime
integration case is skipped.

The Linux phase-3 entry point supports an ASAN/UBSAN CTest-only build.

## Checkpoint B builds

The RID entry points live one directory above this one. Each requires the
pinned vcpkg checkout and a workflow-allocated package version:

```powershell
./native/libraw/build-win-x64.ps1 -VcpkgRoot /path/to/vcpkg -PackageVersion 0.22.2.123
./native/libraw/build-linux-x64.ps1 -VcpkgRoot /path/to/vcpkg -PackageVersion 0.22.2.123
./native/libraw/build-osx-arm64.ps1 -VcpkgRoot /path/to/vcpkg -PackageVersion 0.22.2.123
```

The scripts reject any vcpkg revision other than
`c4d9956c0c10a4742840a5e7d93efa2e0015c865`. Outputs contain a minimal
`runtime/` dependency closure, CTest and validation logs, build options,
licenses, hashes, and `provenance.json`. Linux sanitizer validation uses the
same Linux entry point with `-Sanitizers`; it runs CTest and does not produce a
distributable runtime. Revision `0.22.2.0` is reserved for explicitly
non-candidate developer builds; workflow candidates must use a positive,
never-reused `github.run_number` revision.

Run the paired baseline/candidate performance processes after the candidate
runtime is staged:

```powershell
./native/libraw/run-performance.ps1 -RuntimeDirectory artifacts/libraw/win-x64/runtime -OutputDirectory artifacts/libraw/win-x64/performance
```

The comparison fails when at least two of three paired samples and their median
show an elapsed-time or native-process peak-memory regression above 10%.
Managed-host absolute peaks, host baselines, and deltas remain in both reports
as context but are not a gate because the baseline and candidate test hosts
have different heaps. The accepted memory gate runs lean native probes against
the audited 0.21.1 runtime and the staged bridge candidate and records their
absolute platform peak plus pre-decode host baseline.
Finally, `assemble_candidate.py` consumes all three per-RID artifact roots and
creates the uncommitted multi-RID `.nupkg` candidate plus combined provenance.
