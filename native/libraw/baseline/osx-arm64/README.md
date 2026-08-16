# Apple Silicon ABI-23 baseline runtime

This audited binary is retained only for the isolated native performance
baseline. It is not referenced by an application or test project and is never
loaded in-process with the ABI-25 production bridge.

`libraw.23.dylib` is LibRaw 0.21.1 for arm64 macOS, built with a minimum
deployment target of macOS 13. It statically includes libjpeg-turbo 2.1.5.1
with the libjpeg v8 API and has no non-system dynamic dependencies.

Source archives are available from the
[LibRaw release server](https://www.libraw.org/data/LibRaw-0.21.1.tar.gz) and
[libjpeg-turbo release tag](https://github.com/libjpeg-turbo/libjpeg-turbo/releases/tag/2.1.5.1):

- `LibRaw-0.21.1.tar.gz`: SHA-256
  `630a6bcf5e65d1b1b40cdb8608bdb922316759bfb981c65091fec8682d1543cd`
- `libjpeg-turbo-2.1.5.1.tar.gz`: SHA-256
  `61846251941e5791005fb7face196eec24541fce04f12570c308557529e92c75`

libjpeg-turbo was built static with `WITH_JPEG8=ON`, `WITH_SIMD=OFF`, and
`WITH_TURBOJPEG=OFF`. LibRaw was built shared with JPEG enabled and OpenMP,
Jasper, Little CMS, examples, and its static library disabled. The dylib was
stripped with `strip -x` and its install name set to
`@rpath/libraw.23.dylib`.

Bundled binary SHA-256:
`f9a2ca9cebd3ddbf134123f8dab0a0a3b67d4cbe44b459346d31fc089b4f89b6`.

LibRaw is redistributed under LGPL-2.1-only. See the repository's `licenses/`
directory and `THIRD_PARTY_NOTICES.md` for license texts and source links.
