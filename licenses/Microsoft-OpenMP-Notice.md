# Microsoft Visual C++ OpenMP runtime

`vcomp140.dll` is copyright Microsoft Corporation. All rights reserved.
Happy Photon ships the unmodified x64 file (14.51.36247.0) supplied by
`Magick.NET-Q16-OpenMP-x64` 14.15.0 under `runtimes/win-x64/native/`.
It is app-local (embedded in the Windows single-file publish) and shared by
ImageMagick and LibRaw. It is not covered by Magick.NET's Apache-2.0 license.

Microsoft's Visual Studio license terms govern redistribution of this runtime.
The [Visual Studio 2026 distributable-code list](https://learn.microsoft.com/en-us/visualstudio/releases/2026/redistribution#visual-c-runtime-files)
covers Visual C++ runtime files under `VC/redist`, including the OpenMP runtime,
subject to those terms. See also Microsoft's
[Visual C++ redistribution guidance](https://learn.microsoft.com/en-us/cpp/windows/redistributing-visual-cpp-files).
The release dependency manifest identifies the supplying package and version.
The pinned DLL SHA-256 is
`BC922F83B5D5C31CC4B864DE0925D263B70A52D3651B114213B9DEB15474AEBF`.
