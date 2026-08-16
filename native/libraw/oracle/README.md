# ABI-23 parity oracle

This extractor is compiled against the separately provisioned LibRaw 0.21.1
headers and import library. It must run from an isolated directory containing
only the audited Sdcb Windows runtime set (`raw_r.dll`, `jpeg8.dll`, `lcms2.dll`,
and `zlib1.dll`). Before it opens a fixture or reads a LibRaw structure it checks
that the loaded module resolves beside the executable, has numeric version
0.21.1, and has the audited SHA-256 recorded in
`licenses/LibRaw-runtime-audit.md`.

The generated JSON records the runtime assertions plus the source facts used by
the bridge parity gate. The ABI-23 oracle is a separate process and is never
co-loaded with the ABI-25 bridge.

Restore the Windows oracle runtime explicitly with
`native/libraw/fetch-baseline-runtime.ps1 -RuntimeIdentifier win-x64`. The
script downloads the audited Sdcb runtime package named in the runtime audit,
verifies its package SHA-256 before extraction, and stages it outside every
NuGet project graph. Linux uses the same command with `linux-x64`; the audited
macOS baseline dylib is retained under `native/libraw/baseline/osx-arm64/`.
