# Third-Party Notices

Happy Photon is licensed under GPL-3.0-or-later. The dependencies listed here
remain under their own licenses. This notice describes the dependency versions
locked for the `v0.1.0` preparation branch.

## Direct managed dependencies

| Component | Locked version | License |
| --- | ---: | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 12.0.4 | MIT |
| Avalonia.Controls.ItemsRepeater | 12.0.0 | MIT |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| Magick.NET-Q16-AnyCPU | 14.15.0 | Apache-2.0 |
| Microsoft.Data.Sqlite | 10.0.9 | MIT |
| Microsoft.NET.ILLink.Tasks | 10.0.8 | MIT |
| ModelContextProtocol and ModelContextProtocol.AspNetCore | 1.4.1 | Apache-2.0 |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 | Apache-2.0 |
| Sdcb.LibRaw | 0.21.1.7 | MIT |
| Sdcb.LibRaw native runtimes | 0.21.1 | LGPL-2.1-only OR CDDL-1.0 |

The canonical Apache-2.0 text is distributed in
[`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt). MIT notices are retained
through package metadata and the component-specific notices below. The release
dependency manifest enumerates transitive packages and their locked versions.

## Magick.NET and ImageMagick

Happy Photon uses Magick.NET 14.15.0, copyright Dirk Lemstra, under
Apache-2.0. The package embeds ImageMagick and supporting codec libraries.

The complete notice shipped by the exact NuGet package—including the
ImageMagick license and notices for bundled codec libraries—is preserved
verbatim in
[`licenses/Magick.NET-Notice.txt`](licenses/Magick.NET-Notice.txt). Source and
binary release archives must include that file.

## Sdcb.LibRaw and LibRaw

The managed Sdcb.LibRaw 0.21.1.7 wrapper is copyright Zhou Jie and licensed
under MIT. Its license is in
[`licenses/Sdcb.LibRaw-MIT.txt`](licenses/Sdcb.LibRaw-MIT.txt).

The Windows and Linux runtime packages and the bundled Apple Silicon dylib contain
LibRaw 0.21.1 native binaries and declare `LGPL-2.1-only OR CDDL-1.0`. Happy Photon selects
**LGPL-2.1-only** as its redistribution basis. The LGPL text is in
[`licenses/LGPL-2.1.txt`](licenses/LGPL-2.1.txt), and upstream copyright and
acknowledgement terms are in
[`licenses/LibRaw-COPYRIGHT.txt`](licenses/LibRaw-COPYRIGHT.txt).

The corresponding unmodified LibRaw source is available from the
[LibRaw 0.21.1 source tag](https://github.com/LibRaw/LibRaw/tree/0.21.1).
Every binary release must retain a durable corresponding-source path or
written offer appropriate to the selected LGPL terms.

The runtime packages also contain supporting native libraries. The exact
Platform files, versions, package/source hashes, and binary hashes are recorded
in
[`licenses/Sdcb.LibRaw-runtime-audit.md`](licenses/Sdcb.LibRaw-runtime-audit.md).
The release includes the applicable libjpeg-turbo/IJG, Little CMS, zlib, GPLv3,
and GCC Runtime Library Exception notices.

## SQLite

Microsoft.Data.Sqlite uses SQLite through SQLitePCLRaw. SQLite is dedicated to
the public domain. Managed wrappers and bundled native packaging remain under
the licenses declared by their respective NuGet packages.

## Release requirement

`LICENSE`, `THIRD_PARTY_NOTICES.md`, and the complete `licenses/` directory
must ship in every source and binary artifact. Generated dependency manifests
support this notice but do not replace it.
