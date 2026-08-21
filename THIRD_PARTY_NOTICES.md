# Third-Party Notices

Happy Photon is licensed under GPL-3.0-or-later. The dependencies listed here
remain under their own licenses. This notice describes the dependency versions
pinned by the committed package lock files.

## Direct managed dependencies

| Component | Locked version | License |
| --- | ---: | --- |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent | 12.0.4 | MIT |
| Avalonia.Controls.ItemsRepeater | 12.0.0 | MIT |
| CommunityToolkit.Mvvm | 8.4.2 | MIT |
| Magick.NET-Q16-AnyCPU | 14.15.0 | Apache-2.0 |
| MetadataExtractor | 2.9.3 | Apache-2.0 |
| Microsoft.Data.Sqlite | 10.0.9 | MIT |
| Microsoft.NET.ILLink.Tasks | 10.0.8 | MIT |
| ModelContextProtocol and ModelContextProtocol.AspNetCore | 1.4.1 | Apache-2.0 |
| SQLitePCLRaw.bundle_e_sqlite3 | 3.0.3 | Apache-2.0 |
| HappyPhoton.LibRaw.Native | 0.22.2.11 | GPL-3.0-or-later; bundled components retain their own terms |

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

## Happy Photon LibRaw native package

`HappyPhoton.LibRaw.Native` 0.22.2.11 supplies the Happy Photon bridge and
LibRaw 0.22.2 runtimes selected by NuGet for Windows x64, Linux x64, and Apple
Silicon macOS. The package was built from the audited sources and provenance
recorded in [`licenses/LibRaw-runtime-audit.md`](licenses/LibRaw-runtime-audit.md).

LibRaw declares `LGPL-2.1-only OR CDDL-1.0`. Happy Photon selects
**LGPL-2.1-only** as its redistribution basis. The LGPL text is in
[`licenses/LGPL-2.1.txt`](licenses/LGPL-2.1.txt), and upstream copyright and
acknowledgement terms are in
[`licenses/LibRaw-COPYRIGHT.txt`](licenses/LibRaw-COPYRIGHT.txt).

The corresponding unmodified LibRaw source is available from the
[LibRaw 0.22.2 source tag](https://github.com/LibRaw/LibRaw/tree/0.22.2).
Every binary release must retain a durable corresponding-source path or
written offer appropriate to the selected LGPL terms.

The native package also contains supporting native libraries. The exact
Platform files, versions, package/source hashes, and binary hashes are recorded
in
[`licenses/LibRaw-runtime-audit.md`](licenses/LibRaw-runtime-audit.md).
The bundled components are libjpeg-turbo 3.2.0
([notice](licenses/libjpeg-turbo-3.2.0.txt)), Little CMS 2.19.1
([notice](licenses/Little-CMS-MIT.txt)), and zlib 1.3.2
([notice](licenses/zlib-1.3.2.txt)) on Windows and Linux; the Apple
Silicon runtime links JPEG statically and uses system zlib, so it bundles
only the bridge and LibRaw. Each notice is a verbatim copy of the text
produced by the audited build.

Linux links the system `libgomp.so.1` rather than redistributing it, so
no libgomp binary ships. The GPLv3 and GCC Runtime Library Exception
texts are retained because the native package declares
`GPL-3.0-or-later` as its own license expression. The former Sdcb managed
wrapper notice was removed with the wrapper because no copied Sdcb code
remains.

## MetadataExtractor and XmpCore

Happy Photon uses MetadataExtractor 2.9.3, copyright Drew Noakes 2002-2026,
under Apache-2.0 (text in [`licenses/Apache-2.0.txt`](licenses/Apache-2.0.txt))
to read EXIF tags that the decode libraries do not surface. Its transitive
dependency XmpCore 6.1.10.1 is a .NET port of Adobe's XMP SDK under the Adobe
BSD license, preserved in
[`licenses/XmpCore-BSD.txt`](licenses/XmpCore-BSD.txt).

## SQLite

Microsoft.Data.Sqlite uses SQLite through SQLitePCLRaw. SQLite is dedicated to
the public domain. Managed wrappers and bundled native packaging remain under
the licenses declared by their respective NuGet packages.

## Release requirement

`LICENSE`, `THIRD_PARTY_NOTICES.md`, and the complete `licenses/` directory
must ship in every source and binary artifact. Generated dependency manifests
support this notice but do not replace it.
