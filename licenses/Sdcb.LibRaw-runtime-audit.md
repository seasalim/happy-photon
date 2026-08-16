# Sdcb.LibRaw native runtime audit

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

- LibRaw is redistributed under LGPL-2.1-only. The release carries the LGPL
  text and LibRaw copyright/acknowledgements. Corresponding unmodified source:
  <https://github.com/LibRaw/LibRaw/tree/0.21.1>.
- libjpeg-turbo uses its IJG, modified BSD, and zlib license combination.
  Sources: <https://github.com/libjpeg-turbo/libjpeg-turbo/tree/2.1.3> and
  <https://github.com/libjpeg-turbo/libjpeg-turbo/tree/2.1.5.1>.
- Little CMS uses the MIT license. Sources:
  <https://github.com/mm2/Little-CMS/tree/lcms2.12> and
  <https://github.com/mm2/Little-CMS/tree/lcms2.14>.
- zlib uses the zlib license. Source:
  <https://github.com/madler/zlib/tree/v1.2.11>.
- libgomp is covered by GPLv3 plus the GCC Runtime Library Exception 3.1. The
  release carries both texts. Source:
  <https://github.com/gcc-mirror/gcc/tree/releases/gcc-11.3.0/libgomp>.

This audit applies to the native contents embedded into Happy Photon's
self-contained Windows, Linux, and Apple Silicon executables.

## 2026-08-15 — LibRaw 0.22.2 Checkpoint A

Checkpoint A reconciles the current 0.21.1 baseline before the decoder upgrade.
It changes no runtime or decode behavior. The Windows baseline was refreshed on
2026-08-15; the Linux and macOS refresh remains explicitly open as described
below.

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

The Linux/macOS dated probe refresh is deferred by intake decision and is the
one open Checkpoint A item. Run the same isolated baseline command on
`linux-x64` and `osx-arm64`; live-loaded-module lookup on those hosts is the
remaining platform integration coverage. Until then, standing evidence must
not be described as a refreshed August performance baseline.

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

**Checkpoint A decision:** the Windows baseline, turn-key probe, binary-parser
coverage, compressed-DNG external qualification, source/toolchain pins, target
RID contract, publish inventory, and cache rule are ready for user review. Pause
here for approval. The deferred Linux/macOS refresh remains open; no bridge,
native build, packaging, integration, CI, or production behavior work starts at
this checkpoint.
