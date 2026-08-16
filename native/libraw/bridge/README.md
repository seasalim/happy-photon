# Happy Photon LibRaw bridge

This directory defines bridge ABI version 1. All ABI values use fixed-width
integers and the public header is consumable as C. Inputs described as UTF-8
are pointer-plus-length values: embedded NUL and malformed UTF-8 are rejected.
Fixed textual outputs contain a byte length and replace invalid native bytes
with U+FFFD.

Handles, mosaic leases, and image allocations are monotonically issued tokens.
Unknown, stale, and already-released tokens are rejected. A mosaic borrow is
allocation-free; release it before process, recycle, or close. Those operations
return `HPLR_E_BUSY` immediately while a borrow or another handle operation is
active. Image allocations have exactly one matching `hplr_free_image` call.
No pointer returned by this bridge is valid after its owning token is released.

Errors are per-call caller-owned values. LibRaw failures preserve its numeric
code and text. ABI failures, programming/ownership failures, and bridge
resource/internal failures use distinct classes. `HPLR_ABSENT` (missing
metadata) and `HPLR_UNAVAILABLE` (a non-ushort mosaic layout) are non-errors.

The Windows development build selects `libraw::raw_r`. Other platforms default
to `libraw::raw`, preserving the macOS decision for Checkpoint B. Non-reentrant
builds serialize LibRaw calls globally; every build rejects concurrent work on
the same handle rather than waiting.

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

ASAN/UBSAN execution is deferred to the Linux baseline refresh in phase 3.
