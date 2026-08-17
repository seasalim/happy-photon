# LibRaw native runtime audit

> **Current shipped runtime: `HappyPhoton.LibRaw.Native` 0.22.2.7
> (LibRaw 0.22.2).** The sections immediately below document the
> SUPERSEDED Sdcb-based 0.21.1 baseline and are retained for provenance
> history only. Sdcb was removed in `975e118`. For what ships today, see
> the 0.22.2.7 qualification, per-RID contents, and package hashes later
> in this document, and the "Redistribution basis and sources" section,
> which always describes the CURRENT release.

## Superseded 0.21.1 baseline (historical)

The Windows/Linux packages were audited on 2026-07-23 from the exact NuGet
archives restored by `packages.lock.json`; the Apple Silicon runtime was added
and audited on 2026-08-02. Both packages are version 0.21.1 and are signed NuGet
packages. The corresponding Sdcb release is
[`0.21.1.7`](https://github.com/sdcb/Sdcb.LibRaw/releases/tag/0.21.1.7),
commit `2cfca220ae4632952fd06896e3a850abe498a08b`.

| Package | NuGet archive SHA-256 |
| --- | --- |
| `Sdcb.LibRaw.runtime.win64` 0.21.1 | `92DC2418F4DD888AB4E0FB6CB7D8FDA98B9ECA94EBFC00EAE9F216883DA16EB8` |
| `Sdcb.LibRaw.runtime.linux64` 0.21.1 | `E1F771B685610FCE195FC591456FA4BBF567AE28E540E47BC0AC1A170F1367C9` |

## Windows x64 contents

| File | Identified component | SHA-256 |
| --- | --- | --- |
| `raw_r.dll` | LibRaw 0.21.1 Release | `F500C0732FEB21B188D5B52CEA05FD824D5B3C8016EB2CA68D8312ACC9F914B9` |
| `jpeg8.dll` | libjpeg-turbo 2.1.3, libjpeg API 8.2.2 | `3854CEBC61FD5892CB60491719834922030097F7848D29CCCBFCA3E88D7ED1E5` |
| `lcms2.dll` | Little CMS 2.12 | `87BF94F27F345384420383F247178F825C0101928FEDD3A4887FF60E9D4A76EB` |
| `zlib1.dll` | zlib 1.2.11 | `4A0BD02C2985974F90DA4A7065ACAC1B72F21A4FE58C5391E14B3E6FE566BC12` |

Versions were read from exported version functions and Windows version
resources in the restored binaries.

## Linux x64 contents

| File | Identified component | SHA-256 |
| --- | --- | --- |
| `libraw_r.so.23` | LibRaw 0.21.1 Release | `F42039E9865385F64B708182B5ACA59D39FEB0608467E666103788D3B782E042` |
| `libjpeg.so.8` | libjpeg-turbo 2.1.5.1, build 20230619 | `6C19D2B7C854FC254142FBCA91108B52661909B772351E088E6A78817ED7CA76` |
| `liblcms2.so` | Little CMS 2.14 | `4740689A50F77372FAFCF195D991DF435C84CC9016753FC6C49DA82A76A2467D` |
| `libgomp.so.1` | GCC 11.3.0, Ubuntu 22.04 build | `08E37347737BC95A403E2F6177DBB3B45E4995FF2DE1E6D50BAA2267DEB30BD2` |

Versions were read from embedded source/build strings and exported function
machine constants in the restored ELF binaries. The package publisher states
that native packages are built with vcpkg and supports this Linux package on
Ubuntu 22.04.

## Apple Silicon macOS contents

| File | Identified component | SHA-256 |
| --- | --- | --- |
| `libraw.23.dylib` | LibRaw 0.21.1 Release with static libjpeg-turbo 2.1.5.1 | `F9A2CA9CEBD3DDBF134123F8DAB0A0A3B67D4CBE44B459346D31FC089B4F89B6` |

The arm64 dylib is built from the unmodified LibRaw 0.21.1 source archive
(`630A6BCF5E65D1B1B40CDB8608BDB922316759BFB981C65091FEC8682D1543CD`)
with the unmodified libjpeg-turbo 2.1.5.1 source archive
(`61846251941E5791005FB7FACE196EEC24541FCE04F12570C308557529E92C75`).
It targets macOS 13 or newer, uses `@rpath/libraw.23.dylib` as its install name,
and dynamically links only Apple's `libSystem` and `libc++`. Full build options are
recorded beside the binary in `runtimes/osx-arm64/native/README.md`.

## Redistribution basis and sources

This section describes the CURRENT shipped release
(`HappyPhoton.LibRaw.Native` 0.22.2.7). Component versions come from the
package's committed `…provenance.json`, and the notice texts in
`licenses/` are verbatim copies of the notices produced by that build.

- LibRaw is redistributed under LGPL-2.1-only. The release carries the LGPL
  text and LibRaw copyright/acknowledgements. Corresponding unmodified source:
  <https://github.com/LibRaw/LibRaw/tree/0.22.2>.
- libjpeg-turbo **3.2.0** uses its IJG and modified 3-clause BSD license
  combination — note that 3.x carries TWO compatible BSD-style licenses,
  not the three described for the 2.1.x line. Notice:
  [`licenses/libjpeg-turbo-3.2.0.txt`](libjpeg-turbo-3.2.0.txt). Source:
  <https://github.com/libjpeg-turbo/libjpeg-turbo/tree/3.2.0>.
- Little CMS **2.19.1** uses the MIT license. Notice:
  [`licenses/Little-CMS-MIT.txt`](Little-CMS-MIT.txt). Source:
  <https://github.com/mm2/Little-CMS>.
- zlib **1.3.2** uses the zlib license. Notice:
  [`licenses/zlib-1.3.2.txt`](zlib-1.3.2.txt). Source:
  <https://github.com/madler/zlib>.
- libgomp is **NOT redistributed**. Linux builds link the runner's system
  `libgomp.so.1`, which Checkpoint B approved as a system prerequisite
  rather than a bundled component. The GPLv3 and GCC Runtime Library
  Exception texts are retained in `licenses/` because the native package
  declares `GPL-3.0-or-later` as its own license expression, not because
  libgomp ships inside it.

Historical note: the superseded 0.21.1 baseline bundled libjpeg-turbo
2.1.3/2.1.5.1, Little CMS 2.12/2.14, and zlib 1.2.11. Those notices were
replaced when the runtime moved to 0.22.2.7.

This audit applies to the native contents embedded into Happy Photon's
self-contained Windows, Linux, and Apple Silicon executables.

## 2026-08-15 — LibRaw 0.22.2 Checkpoint A

Checkpoint A reconciles the current 0.21.1 baseline before the decoder upgrade.
It changes no runtime or decode behavior. The Windows baseline was refreshed on
2026-08-15, and the deferred Linux and macOS runner-class refresh completed on
2026-08-16.

### Reproducible baseline probe

`LibRawBaselineProbeTests.CurrentRid_EmitsCompleteLibRawBaseline` is the single
accepted source of RAW timing and peak-memory measurements. It is skipped by
default, fails rather than reports memory unless the isolated-run marker is set,
and requires only the .NET 10 SDK:

```powershell
$env:HAPPY_PHOTON_BASELINE='1'
$env:HAPPY_PHOTON_BASELINE_ISOLATED='1'
dotnet test Tests/HappyPhoton.Tests.csproj --configuration Release `
  --no-build --no-restore `
  --filter "FullyQualifiedName=HappyPhoton.Tests.LibRawBaselineProbeTests.CurrentRid_EmitsCompleteLibRawBaseline" `
  --logger "console;verbosity=detailed"
```

The second variable is an assertion that the exact filter is being used, not a
second diagnostic gate. The probe reads version and capability data through
Sdcb's existing managed surface, locates the already-loaded module, and never
performs a fresh name-based native load. It emits one block containing hashes,
architecture, imports, install identity/SONAME where present, encoded binary
requirements, and bundled/OS-provided/prerequisite classifications. Its managed
PE, ELF64, and Mach-O parsers are unit-tested against the restored Windows and
Linux runtime packages and the checked-in macOS dylib. Host inspection tools are
optional cross-checks and are not part of the procedure.

### Refreshed win-x64 baseline

Environment: Windows `10.0.26200`, x64 process, .NET `10.0.11`, AMD64 Family 23
Model 113 Stepping 0, 24 logical processors, and 31.9 GiB of available memory as
reported by the .NET GC. These are machine-local comparison numbers, not gates.

| Fact | 2026-08-15 result |
| --- | --- |
| LibRaw version | numeric `0.21.1`; string `0.21.1-Release` |
| Capability mask | `0x000000C0` |
| Named capabilities | `Zlib=true`, `Jpeg=true`; `RawSpeed`, `DngSdk`, `GprSdk`, `UnicodePaths`, `X3fTools`, `Rpi6By9`, `RawSpeed3`, and `RawSpeedBits` all `false` |
| LCMS | present through the direct `lcms2.dll` import and resolved bundled dependency; this is not a capability bit |
| OpenMP | present through the direct `VCOMP140.DLL` import and resolved prerequisite; this is not a capability bit |
| Preview decode | `canon-eos-350d.cr2`, one preview plus one full warm-up: 494.4 ms, 1600x1066, `RawLibRaw` asserted |
| Full decode | `canon-eos-350d.cr2`: 801.8 ms, 3474x2314, `RawLibRaw` asserted |
| Full export | `fujifilm-x30.raf`, JPEG quality 85, chroma NR 100, one full-export warm-up, 10 ms sampling: 2140.6 ms, 4032x3012, sampled peak `PrivateMemorySize64` delta 421,769,216 bytes (402.2 MiB), `RawLibRaw` asserted by the loader from that invocation |

The loaded module was
`Tests/bin/Release/net10.0/runtimes/win-x64/native/raw_r.dll`, PE x86-64,
SHA-256 `F500C0732FEB21B188D5B52CEA05FD824D5B3C8016EB2CA68D8312ACC9F914B9`.
Its encoded PE OS and subsystem versions are both 6.0. Those header values are
recorded as encoded binary requirements, not inferred publisher support floors.
No separate publisher-declared Windows floor is asserted here.

Direct imports were `WS2_32.dll`, `lcms2.dll`, `zlib1.dll`, `jpeg8.dll`,
`KERNEL32.dll`, `MSVCP140.dll`, `VCOMP140.DLL`, `VCRUNTIME140.dll`,
`VCRUNTIME140_1.dll`, and the required `api-ms-win-crt-*` runtime families.
The four bundled binaries and their hashes are listed in **Windows x64
contents** above. The loaded x86-64 OpenMP prerequisite was
`C:\Windows\System32\VCOMP140.DLL`, SHA-256
`95D4CE4A6802D1E18B5E0E1722CC30EA72CA7E033F83828F05C0B7B993FE7CBF`;
it imports only OS-provided `KERNEL32.dll` and also encodes PE OS/subsystem 6.0.
Windows and Universal CRT imports are classified as OS-provided.

The locked Release verification completed with 0 failures. `HappyPhoton.Tests`
reported 1,071 passed and 6 expected opt-in diagnostics skipped;
`HappyPhoton.Headless.Tests` reported 114 passed and 0 skipped (1,185 passed and
6 skipped across the solution). This includes
`RawBaseLoaderTests.CanonPreviewAndFull_AreDeterministicAndMeasured`, the
`PreviewBase_IsLinearSixteenBitAndCarriesRawFacts` and
`PreviewAndFull_EstimatesAgreeWithinTolerance` RAW fixture theories, and the
golden/WYSIWYG RAW cases. These tests instantiate `RawBaseLoader` directly and
assert `BaseSourceKind.RawLibRaw`, so `BaseLoaderRouter` cannot satisfy them via
the Magick.NET fallback. `NativeRuntime_IsAvailableAndVersionMatched` is useful
version/`CanLoad` evidence but is not, by itself, decode-path proof.

Verification used the CONTRIBUTING sequence: locked-mode restore, Release build
without restore, then Release solution tests without build or restore. The
sandbox could not reach NuGet.org, so the successful locked restore used a
temporary local feed containing the already-cached `MetadataExtractor` and
`XmpCore` archives; all other packages came from the global cache. No lock file
or package selection changed.

### Refreshed linux-x64 baseline

The deferred probe ran on 2026-08-16 on the GitHub-hosted `ubuntu-22.04`
runner class. The host reported Ubuntu 22.04.5 LTS, an x64 process, .NET
10.0.11, 4 logical processors, and 15.6 GiB of GC-available memory. These
runner-class measurements are comparison context, not absolute performance
gates.

| Fact | 2026-08-16 result |
| --- | --- |
| LibRaw version | numeric `0.21.1`; string `0.21.1-Release` |
| Capability mask | `0x000000C0` |
| Named capabilities | `Zlib=true`, `Jpeg=true`; all other reported capability bits `false` |
| LCMS/OpenMP | both present in the dependency graph; neither is represented by a capability bit |
| Preview decode | `canon-eos-350d.cr2`, one preview plus one full warm-up: 484.3 ms, 1600x1066, `RawLibRaw` asserted |
| Full decode | `canon-eos-350d.cr2`: 871.6 ms, 3474x2314, `RawLibRaw` asserted |
| Full export | `fujifilm-x30.raf`, JPEG quality 85, chroma NR 100, one full-export warm-up, 10 ms sampling: 4924.7 ms, 4032x3012, sampled peak `PrivateMemorySize64` delta 473,833,472 bytes, `RawLibRaw` asserted |

The live-loaded module was
`Tests/bin/Release/net10.0/runtimes/linux-x64/native/libraw_r.so.23`, ELF64
x86-64 with SONAME `libraw_r.so.23`, SHA-256
`F42039E9865385F64B708182B5ACA59D39FEB0608467E666103788D3B782E042`.
Its direct imports were `liblcms2.so`, `libz.so.1`, `libjpeg.so.8`,
`libgomp.so.1`, `libstdc++.so.6`, `libm.so.6`, `libgcc_s.so.1`, and
`libc.so.6`. The probe recorded this dependency inventory:

| Dependency | Classification | Probe resolution and SHA-256 |
| --- | --- | --- |
| `libraw_r.so.23` | bundled | loaded path above; `F42039E9865385F64B708182B5ACA59D39FEB0608467E666103788D3B782E042` |
| `liblcms2.so` | bundled | package runtime; `4740689A50F77372FAFCF195D991DF435C84CC9016753FC6C49DA82A76A2467D` |
| `libjpeg.so.8` | bundled | package runtime; `6C19D2B7C854FC254142FBCA91108B52661909B772351E088E6A78817ED7CA76` |
| `libgomp.so.1` | bundled | package runtime; `08E37347737BC95A403E2F6177DBB3B45E4995FF2DE1E6D50BAA2267DEB30BD2` |
| `libz.so.1` | prerequisite | unresolved by the bounded probe; hash not asserted |
| `libstdc++.so.6` | prerequisite | unresolved by the bounded probe; hash not asserted |
| `libgcc_s.so.1` | prerequisite | `/usr/lib/x86_64-linux-gnu/libgcc_s.so.1`; `FC9D43B2F6C20E53B009238F767C5B949D202389E20DE9E202EA684B4BA3729A` |
| `libm.so.6`, `libc.so.6`, `ld-linux-x86-64.so.2` | OS-provided | unresolved by the bounded probe; hashes not asserted |

The encoded symbol requirements reached `GLIBC_2.33` in LibRaw and
`GLIBC_2.35` in the runner-provided `libgcc_s.so.1`; these are binary facts,
while Ubuntu 22.04 remains the publisher-provenance compatibility floor.

### Refreshed osx-arm64 baseline

The deferred probe ran on 2026-08-16 on the GitHub-hosted `macos-15` runner
class. The host reported macOS 15.7.7, an arm64 process, .NET 10.0.11, 3
logical processors, and 7 GiB of GC-available memory. These runner-class
measurements are comparison context, not absolute performance gates.

| Fact | 2026-08-16 result |
| --- | --- |
| LibRaw version | numeric `0.21.1`; string `0.21.1-Release` |
| Capability mask | `0x00000080` |
| Named capabilities | `Jpeg=true`; `Zlib=false` and all other reported capability bits `false` |
| LCMS/OpenMP | both absent from the dependency graph |
| Preview decode | `canon-eos-350d.cr2`, one preview plus one full warm-up: 361.5 ms, 1600x1066, `RawLibRaw` asserted |
| Full decode | `canon-eos-350d.cr2`: 546.5 ms, 3474x2314, `RawLibRaw` asserted |
| Full export | `fujifilm-x30.raf`, JPEG quality 85, chroma NR 100, one full-export warm-up, 10 ms sampling: 6265.8 ms, 4032x3012, `RawLibRaw` asserted; the platform reported zero for both sampled private-memory deltas, so those values are context only |

The live-loaded module was
`Tests/bin/Release/net10.0/libraw.23.dylib`, Mach-O 64 arm64 with install name
`@rpath/libraw.23.dylib`, SHA-256
`F9A2CA9CEBD3DDBF134123F8DAB0A0A3B67D4CBE44B459346D31FC089B4F89B6`.
It encodes macOS 13.0.0 as its minimum and imports only the OS-provided
`/usr/lib/libSystem.B.dylib` and `/usr/lib/libc++.1.dylib`; the bounded probe
did not resolve or hash either system library. JPEG is compiled into the
single bundled LibRaw dylib. No separate LCMS, OpenMP, JPEG, or zlib library
is bundled.

### Compressed-DNG qualification

No redistributable candidate fit the asset budget. `Tests/assets/` was
84,336,384 bytes before this work, leaving 598,272 bytes under its 81 MiB cap;
both candidates exceed that headroom and neither source states a redistribution
license. No fixture was committed and there is no silent waiver. This follows
the required search order: no compact upstream candidate survived
the license and budget gates; deterministic deflate generation remains possible
with a pinned FOSS encoder, but no redistributable encoder was found for lossy
compression 34892 and generation would not fit the current budget. The stable,
external corpus is therefore the Checkpoint A outcome.

The controlled external qualification corpus is:

| Compression evidence | Source and license | Fetch date | Bytes | SHA-256 |
| --- | --- | --- | ---: | --- |
| lossy JPEG, TIFF Compression `34892` | [pixls.us forum upload](https://discuss.pixls.us/uploads/short-url/a1SFutMlwzVCyVX9eNJgAi2h2Zr.dng) ([context](https://discuss.pixls.us/t/need-help-for-rendering-lossy-dng/38941)); no license stated, external evidence only | 2026-08-15 | 6,548,376 | `91BE7341D999AE17A3C768CE394BCF9183BB3DD9D88479EF722A510EEC87E01F` |
| Adobe Deflate, TIFF Compression `8` | [RawTherapee shared test image](https://rawtherapee.com/shared/test_images/hdrmerge_045.dng); no license stated, external evidence only | 2026-08-15 | 13,298,976 | `EC304072B464F82D9FD8DDEB47EBE83ECF5B3007C6595C61257EC849F0919A08` |

The tags were independently read on 2026-08-15 by the repository's bounded
classic-TIFF IFD/SubIFD parser (`DngCompressionInspection`, .NET 10.0.11), not
inferred from filenames or LibRaw results. With the expected byte lengths and
hashes asserted first, `CompressedDngQualificationTests` fully decoded both
files through `RawBaseLoader` on win-x64 and asserted `RawLibRaw`. Re-run with:

```powershell
$env:HAPPY_PHOTON_DNG_LOSSY='path/to/lossy-candidate-pixls-38941.dng'
$env:HAPPY_PHOTON_DNG_DEFLATE='path/to/deflate-candidate-hdrmerge_045.dng'
dotnet test Tests/HappyPhoton.Tests.csproj --configuration Release `
  --no-build --no-restore `
  --filter "FullyQualifiedName=HappyPhoton.Tests.CompressedDngQualificationTests.ExternalCorpus_CompressionTagsAndNativeDecodeAreVerified"
```

### Standing platform and publish evidence

- The 2026-07-23 Linux package audit and its hashes above remain standing
  `linux-x64` evidence. The publisher-declared floor remains Ubuntu 22.04; it is
  a provenance fact and is not inferred from ELF headers or symbol versions.
- The 2026-08-02 Apple Silicon audit and build record above remain standing
  `osx-arm64` evidence. The dylib encodes macOS 13.0.0 as its minimum OS,
  `@rpath/libraw.23.dylib` as its install name, and only Apple system imports.
- The gitignored 2026-07-25 checkpoint publishes in the primary checkout were
  re-hashed on 2026-08-15 as external evidence. `win-x64/HappyPhoton.exe` was
  230,135,357 bytes, SHA-256
  `93C1532D55624654FC139F281125191E65AA32A910F1EBF3C631D4968CE4E8D6`;
  `linux-x64/HappyPhoton` was 237,401,112 bytes, SHA-256
  `C7907231D8E8EEA24F46FB4FB0CCF885B190688EA8FBF0689EF676DCCEE756AA`.
- The July macOS publish is packaging-only evidence: it predates the 2026-08-02
  LibRaw dylib and used the Magick.NET RAW path. Its `HappyPhoton` executable was
  209,317,484 bytes, SHA-256
  `2AC840D6F51B4AF3036EDF813025E2692904A03C148B7CD3C7951AC5EF1A0DE5`.
  Native files were `libAvaloniaNative.dylib`
  (`FC0F2192EC0F674E6D60BC023201B7D7F7F9527D676D929AEC525276DE2AB8A8`),
  `libe_sqlite3.dylib`
  (`7B319CD32435AB28C97041FAD74B892BE218E6F0F74790802105309C1EC515A9`),
  `libHarfBuzzSharp.dylib`
  (`B2D7D39A46954B3D7310540BDF393F398AC4194EE029B995DEEB1DB0C161C43D`),
  `libSkiaSharp.dylib`
  (`E09F07AE1DF62DED475351F56D8DC8366CD679043F28B65D9DEE597A5FD0DA6C`),
  and `Magick.Native-Q16-arm64.dll.dylib`
  (`0C6983CB25D92499C694D84C063DCEFAA555C8E8B0B8ABA6BE6DF34879A95EAD`).

The 2026-08-16 isolated `linux-x64` and `osx-arm64` runs above close the one
deferred Checkpoint A item. Both resolved the live-loaded module and emitted
the full runner-class performance and dependency record.

### Accepted source, toolchain, and target contract

Both upstream source archives were fetched directly on 2026-08-15. The SHA-256
values match the independent 2026-08-07 work-package measurements; the archives
are evidence and are not committed.

| Archive | URL | Bytes | SHA-256 | SHA-512 |
| --- | --- | ---: | --- | --- |
| `LibRaw-0.22.2.tar.gz` | <https://www.libraw.org/data/LibRaw-0.22.2.tar.gz> | 1,682,962 | `DE86B035655ACCFF8D4010F1A221FDF50D353CB7B1422BA26F14A0DB92612CFA` | `9333BC667C8E68A3572C336D3E2ECDA82C5987E7FEECB6CEB4E1DF7DC7291747FFE66F6D3E01B121946BA4E2B1BE95295C030D2754A5AE1CD638CFFC8213141A` |
| `LibRaw-0.22.2.zip` | <https://www.libraw.org/data/LibRaw-0.22.2.zip> | 1,826,232 | `F4448371523F9960D26A131ED9DC21BEE49F967ADBCC4FB6F56B9B47C2BA87C6` | `E6BB652A0ED93FBF7331304E2C48728E409F77A664AD416FECB7B4786A8EAECD2B71A8A13F9E7BFE2DAB0177D1AFA5AE12F864ED3B8368008F7D8A852B217DB3` |

The native-build toolchain pin is vcpkg revision
`c4d9956c0c10a4742840a5e7d93efa2e0015c865`. Its port is 0.22.1, so the later
native build requires the reviewed 0.22.2 overlay described by the work package.

| RID | Accepted initial 0.22.2 capability/dependency contract |
| --- | --- |
| `win-x64` | ABI-25 LibRaw and bridge DLL; preserve JPEG, zlib, LCMS, and OpenMP and package/review every non-OS dependency |
| `linux-x64` | ABI-25 LibRaw and bridge SO; preserve JPEG, zlib, LCMS, and OpenMP (`libgomp.so.1`) and retain Ubuntu 22.04 as the provenance-backed declared floor unless a later reviewed build changes it |
| `osx-arm64` | ABI-25 non-`_r` LibRaw dylib and bridge dylib; JPEG remains static, zlib remains through `libSystem`, and LCMS/OpenMP remain absent |

The non-`_r` macOS library is the default. Any `_r` proposal is routed to
Checkpoint B with concurrency, pixel, performance, dependency, and signing
evidence; a build-system default must not make that change implicitly.

The decoder integration must use the next unused `BaseImage.Version` at merge.
It is 2 at this checkpoint, so the likely next value is 3, but it must be
re-verified immediately before merge and must not reuse a namespace consumed by
an intervening decode change.

**Checkpoint A decision and closure:** the Windows baseline, turn-key probe,
binary-parser coverage, compressed-DNG external qualification,
source/toolchain pins, target RID contract, publish inventory, and cache rule
were approved before native candidate work began. The dated Linux/macOS
runner-class refresh is now also complete. This closes the deferred evidence
item without changing the approved 0.22.2 contract.

## 2026-08-16 — LibRaw 0.22.2 Checkpoint B evidence

This section records candidate evidence for user review; it does not approve a
package or authorize integration. Dispatch 6, GitHub Actions run
`31931241271`, built source commit
`b675b2cb1a568aec12959b6ed7d65e615b2c54e8` as candidate `0.22.2.6`.
Windows and macOS completed end to end. Linux build and validation completed,
but the then-fatal elapsed gate stopped its RID artifact upload; the retained
diagnostics contain its validation and performance reports.

### Candidate contract results

All three validation reports identify LibRaw numeric version `0x001602`
(`0.22.2-Release`), bridge ABI 1, capability mask `0x000000C0` (JPEG and zlib),
Release builds, and zero unresolved bridge imports against the corresponding
LibRaw exports. Each RID passed all four CTests: C-header ABI smoke, test-only
native tests, public-ABI smoke against the exact staged production bridge, and
the feature probe. Jasper is disabled everywhere.

| RID | Contract result | Candidate runtime and loader evidence |
| --- | --- | --- |
| `win-x64` | Passed: reentrant `_r`, LCMS and OpenMP present and exercised; constrained/parallel probes produced the same checksum; PE x86-64 minimum OS field 6.0 | `happyphoton_libraw_bridge.dll`, `raw_r.dll`, `jpeg8.dll`, `lcms2-2.dll`, and `z.dll`; 12 bridge LibRaw imports resolved against 584 exports |
| `linux-x64` | Passed in run `31931241271`: reentrant `_r`, LCMS and OpenMP present and exercised; constrained/parallel probes produced the same checksum; ELF x86-64 symbol-version ceiling `GLIBC_2.33` | `$ORIGIN` resolution selected the staged `libraw_r.so.25`, `liblcms2.so.2`, `libjpeg.so.8`, and `libz.so.1`; runner `libgomp.so.1` remained the approved prerequisite; 14 bridge LibRaw imports resolved against 559 exports |
| `osx-arm64` | Passed: non-reentrant, LCMS/OpenMP absent, static JPEG and system zlib; Mach-O arm64 minimum macOS 13.0 | both install names are package-local: `@loader_path/libhappyphoton_libraw_bridge.dylib` and `@loader_path/libraw.25.dylib`; imports are limited to those identities plus `libSystem`, `libc++`, and system zlib; 14 bridge LibRaw imports resolved against 543 exports |

The staged runtime hashes from dispatch 6 are:

| RID | File | SHA-256 |
| --- | --- | --- |
| `win-x64` | `happyphoton_libraw_bridge.dll` | `CD09ABCE2F939F0ED5673EB89E603C200EE4AD93DBB774C58330BDFCF2C39468` |
| `win-x64` | `raw_r.dll` | `79FD95A216FC3FEB11CB740203D40537724D5D2A6FDD509BF0DD007D6B3BAE31` |
| `linux-x64` | `libhappyphoton_libraw_bridge.so` | `F8F90828D6E640B9FA5182FBB5DCE24A8310BB74F2B89FF521893C3C98B72830` |
| `linux-x64` | `libraw_r.so.25` | `C36261B02F438DF9C918597F508D654F9F218CD72E3F7A954912C9702EDFE86D` |
| `osx-arm64` | `libhappyphoton_libraw_bridge.dylib` | `41001867C9BEF90CF804435E775CE1EADD991F7C47C9717380304130D5DC9218` |
| `osx-arm64` | `libraw.25.dylib` | `7C7551653A0C4F0FCBE0934106CBE0A42599BF7BC615305027DF4F4CF802701E` |

### Performance evidence and ruling

The paired harness runs the 0.21.1 baseline and 0.22.2 candidate sequentially
as separate processes on one runner, using the same fixture, configurations,
warm-up, buffer copy, timing, and sampled process-memory protocol. Native peak
memory is the CI-fatal metric. A repeatable elapsed increase above 10% remains
measured and is emitted as `accepted-elapsed-flagged` for mandatory human
review at Checkpoints B and C, but does not fail the job. Managed-host memory
remains recorded context and is not an accepted gate.

Run 6's Linux native peak memory was flat: +0.096% for `linear16-preview` and
+0.114% for `srgb8-full`. The elapsed variation for the same baseline/candidate
binary pair across shared runners establishes co-tenancy noise rather than a
repeatable decoder regression:

| Dispatch candidate | `linear16-preview` elapsed | `srgb8-full` elapsed | Outcome |
| --- | ---: | ---: | --- |
| `0.22.2.3` | within 10% | within 10% | accepted |
| `0.22.2.5` | within 10% | within 10% | accepted |
| `0.22.2.6` | -8.695% | +11.994% | accepted-elapsed-flagged after investigation |

For additional context, dispatch 6 Windows recorded elapsed +5.353%/-18.248%
and native peak memory +0.397%/+0.218%; macOS recorded elapsed
-35.738%/-24.473% and native peak memory +2.946%/+6.294% (preview/full,
respectively). Local Windows comparisons were consistently faster on 0.22.2.
The application-level export comparison remains deferred to the phase 4/6
gates as approved.

### Revision, workflow, and provenance record

Versions `0.22.2.1` through `0.22.2.6` are permanently consumed by dispatched
iterations, whether a run failed or succeeded. `0.22.2.0` remains reserved for
developer-local builds. A qualifying dispatch must use the next immutable
`github.run_number`; reruns are rejected, and the preflight rejects a version
already represented by a committed package or candidate artifact.

The manual-only workflow is registered by the same-path placeholder on the
default branch and dispatched with `--ref` so the feature branch's full YAML
and exact checkout commit are used. Its pinned three-RID builds validate the
runtime, feature contract, thread behavior, symbols, dependencies,
architecture, loader identities, OS floor, CTests, sanitizers, and paired
performance. Each RID artifact carries sources, toolchain, options, output
hashes, licenses, and the validation-report hash in `provenance.json`; assembly
requires one source commit across all RIDs and emits a multi-RID candidate,
combined provenance, and hashes without committing them.

The qualifying dispatch number and its candidate-package and combined-
provenance SHA-256 values must be appended here after that dispatch completes.
Checkpoint B remains a hard stop: no package commit or decoder integration is
authorized until the user approves the exact three native sets and bridge
ABI/layout evidence.

### Qualifying dispatch record (2026-08-16)

Workflow run 31932196288 (revision `0.22.2.7`) was the first dispatch with
every job green: both 0.21.1 baseline probes, ASAN/UBSAN, all three RID
build/validate/performance jobs, and candidate assembly. Downloaded and
hash-verified by the orchestrator:

| Artifact | SHA-256 |
| --- | --- |
| `HappyPhoton.LibRaw.Native.0.22.2.7.nupkg` (2,585,678 bytes) | `D0E791294B5799FBE20CD438BAA95422BEB28C4982927164AC6A53DF43076CE6` |
| `native-provenance.json` (combined) | `269262976E0EE2AB5C33B518EA89804D64DBE09BEE0D97FF3724C8AE0CB0A610` |

Package runtime contents match the approved per-RID contract: win-x64
bridge + `raw_r.dll` + `jpeg8.dll`/`lcms2-2.dll`/`z.dll`; linux-x64
bridge + `libraw_r.so.25` + `libjpeg.so.8`/`liblcms2.so.2`/`libz.so.1`
(GLIBC symbol ceiling 2.33, within the Ubuntu 22.04 floor); osx-arm64
bridge dylib + canonical `libraw.25.dylib` only. Consumed revisions
`0.22.2.1`–`0.22.2.6` belong to the iteration dispatches recorded above
and are never reused.

Open Checkpoint B decision recorded for the user: linux-x64 OpenMP is
functionally proven (661.6 ms parallel vs 995.5 ms constrained, identical
checksums) but `libgomp.so.1` now resolves as a SYSTEM prerequisite
(`/lib/x86_64-linux-gnu/libgomp.so.1`) rather than being bundled as the
Sdcb 0.21.1 package did — symmetric with the Windows `VCOMP140.dll`
prerequisite treatment, but a delivery change from the 0.21.1 contract
requiring explicit approval or a bundling revision.

**Checkpoint B decision (2026-08-16):** the user approved Checkpoint B —
all three native sets, the bridge ABI/layout evidence, and the recorded
performance ruling — including acceptance of `libgomp.so.1` as a
documented linux-x64 SYSTEM prerequisite (mirroring the Windows
`VCOMP140.dll` treatment) rather than a bundled file. A missing
prerequisite degrades RAW decoding to the Magick.NET fallback with a
diagnostic; it does not prevent application startup. System-requirements
documentation must state the prerequisite (phase 9).

**Superseded 2026-08-16 — single-decoder policy:** the approved fallback above is no
longer production policy. Magick.NET 14.15.0 advertises the `raw` delegate and reports
RAF, CR2, CR3, NEF, DNG, ARW, and ORF through its LibRaw-backed `Dng` module; its
`Magick.Native-Q16-x64.dll` embeds `0.22.1-Release`. That is an older build of the same
library than the audited 0.22.2 runtime, not an independent rescue decoder. The X30 RAF
fixture measured 8.1 seconds through Magick versus 1.9 seconds through the Happy Photon
binding. Its 100% crops had near-identical detail with a small tone shift, making the two
outputs non-interchangeable in shared caches. The embedded build was unaudited,
unversioned in this repository, and invisible to the process health gate. The accepted
residual risk is a hypothetical 0.22.2 regression that 0.22.1 would decode.

Production now fails RAW visibly when the audited runtime is rejected or a file is
unsupported. The former Windows-only RAF exception is redundant and removed: no
original rationale was recorded or reproduced, and the fixture decoded cleanly through
Magick during this investigation without a crash or corrupt output. Caches written
before the single-decoder change could contain Magick/LibRaw-0.22.1 pixels; no automated
repair ships, and the remedy is clearing the cache. With no second RAW pixel producer,
cache keys need no decoder identity. `BaseImage.Version` remains 3 and
`RenderPipeline.Version` is unchanged.

## 2026-08-16 — Phase 4 integration record

Production decoding migrated from Sdcb.LibRaw 0.21.1 to the audited
`HappyPhoton.LibRaw.Native` 0.22.2.7 package via the repository bridge
binding. Sdcb references were removed repository-wide; the 0.21.1 managed
probes retired (their dated baselines above are the permanent record) and
the audited ABI-23 runtimes for the oracle and native performance
baseline now stage from hash-pinned sources, including the relocated
macOS dylib under `native/libraw/baseline/osx-arm64/`.

Application-level comparison against the 2026-08-15 win-x64 baseline
above (same machine and protocol, migrated probe): preview 456.0 ms
(−7.8%), full 731.7 ms (−8.7%), export 2,090.2 ms (−2.4%), export peak
private delta 324,243,456 bytes / 309.2 MiB (−23.1%). The initial
integration showed +70.8% export peak, root-caused to LibRaw 0.22.2's
OpenMP per-worker scratch in full-resolution X-Trans processing plus
context-lifetime overlap; the accepted mitigations are a binding-level
default of `OMP_NUM_THREADS = min(cores, 8)` applied only when the
variable is unset (explicit overrides win; output pixels are
thread-count-invariant per the recorded thread-comparison checksums) and
context recycle before pipeline import. `BaseImage.Version` is 3.

Six RAW goldens changed (three Nikon D70, three Pentax K-r) with CIE76
ΔE mean/p99 up to 8.514/13.695; each was side-by-side reviewed and
individually accepted as LibRaw 0.22.2 demosaic/color-table effects.
Workflow run 31938701422 verified the retired-baseline build workflow and
audited-runtime staging end to end after integration.

## 2026-08-16 — Cache and pixel review closure (LIBRAW_222 step 6)

Step 6 is satisfied by work already recorded above; no separate change
was needed.

- `BaseImage.Version` is 3, bumped from 2 exactly once (commit
  975e118) and never reused, so it remains the next-unused value.
  `RenderPipeline.Version` stays 7: no render-stage math changed.
- All existing RAW goldens and the WYSIWYG cases were run before any
  baseline was accepted. Six changed (three Nikon D70, three Pentax
  K-r); each was reviewed side by side and accepted individually as a
  LibRaw 0.22.2 demosaic/color-table effect, with CIE76 ΔE mean/p99
  recorded above. None was accepted merely because the decoder changed.
- Goldens have not moved since 975e118. The single-decoder change
  (1f2fc09) and the export-reporting change (f56a555) were both verified
  to leave healthy-runtime pixels untouched.
- Outstanding for Checkpoint C only: re-confirm immediately before
  release that 3 is still the next-unused cache value.

## 2026-08-16 — OpenMP worker cap raised to sixteen

The phase-4 default of `min(cores, 8)` was measured against the
modern-camera fixtures this upgrade targets and found to cost real time
on large X-Trans files without a proportionate memory saving. Full
sRGB decode of the Fujifilm X-T50 fixture (40.1 MP X-Trans) on a
24-core Windows machine:

| `OMP_NUM_THREADS` | total | peak working set | peak paged |
| --- | ---: | ---: | ---: |
| 8 (previous default) | 5,637 ms | 594 MiB | 582 MiB |
| 16 (new default) | 3,590 ms | 659 MiB | 771 MiB |
| 24 (all cores) | 3,666 ms | 659 MiB | 959 MiB |

Sixteen recovers 36% of the decode time for about 65 MiB more working
set; beyond sixteen there is no further time gain and paged memory keeps
rising. A Bayer control (Panasonic DC-S9, 24.2 MP) showed the opposite
sensitivity — 1,072 ms at eight workers versus 1,185 ms at 24 — so this
is an X-Trans demosaic cost that scales with resolution, which is why
the 12 MP X30 fixture used in phase 4 did not reveal it.

The cap only binds on machines with more than eight cores, which are
also the machines with the most memory headroom. Explicit
`OMP_NUM_THREADS` still wins, output pixels remain
thread-count-invariant per the recorded thread-comparison checksums, and
the phase-4 recycle-before-import change is unaffected. The relative
contribution of the cap versus that recycle change to the phase-4 export
peak has not been measured separately.
