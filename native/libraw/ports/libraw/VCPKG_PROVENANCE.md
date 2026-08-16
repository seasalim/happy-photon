# vcpkg provenance

This overlay was adapted from `microsoft/vcpkg` at commit
`c4d9956c0c10a4742840a5e7d93efa2e0015c865` for LibRaw 0.22.2. The adjacent
`VCPKG_LICENSE.txt` contains vcpkg's MIT license.

Distributable builds use the LibRaw-hosted `LibRaw-0.22.2.tar.gz` archive,
SHA-512
`9333bc667c8e68a3572c336d3e2ecda82c5987e7feecb6ceb4e1df7dc7291747ffe66f6d3e01b121946ba4e2b1be95295c030d2754a5ae1cd638cffc8213141a`.
Jasper is disabled on every target. LCMS and vcpkg zlib are Windows/Linux-only;
macOS uses the SDK zlib and statically links libjpeg-turbo.
