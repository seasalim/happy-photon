# Sdcb.LibRaw native runtime audit

Audited on 2026-07-23 from the exact NuGet archives restored by
`packages.lock.json`. Both packages are version 0.21.1 and are signed NuGet
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
self-contained Windows and Linux executables. The Apple Silicon build uses
the Magick.NET RAW path and does not ship these Sdcb native runtimes.
