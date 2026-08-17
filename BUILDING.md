# Building Happy Photon

Install the .NET 10 SDK, then run:

```bash
dotnet restore HappyPhoton.sln --locked-mode
dotnet build HappyPhoton.sln --configuration Release --no-restore
dotnet test HappyPhoton.sln --configuration Release --no-build --no-restore
dotnet run --project HappyPhoton.csproj
```

Windows portable publish:

```bash
dotnet publish HappyPhoton.csproj -p:PublishProfile=win-x64
```

Linux portable publish:

```bash
dotnet publish HappyPhoton.csproj -p:PublishProfile=linux-x64
```

Local Apple Silicon app bundle:

```bash
./scripts/package-macos.sh
```

The local macOS script uses ad-hoc signing for development. Public artifacts
must be Developer ID-signed, notarized, and stapled.

## RAW decoding native assets

RAW decoding uses `HappyPhoton.LibRaw.Native`, a repo-owned package
committed under `packages/native/` alongside its provenance file. A fresh
clone needs no extra setup: the locked restore above resolves that local
package and lays the per-RID native binaries down automatically. There is
no separate native build step, and no network fetch of a decoder.

Maintainers rebuild the package **only** through the manually dispatched
`build-libraw` GitHub Actions workflow, which builds all three RIDs from a
pinned vcpkg revision and runs the validation, audit, and performance
gates. Never hand-build a native binary into `packages/native/`: a
workflow artifact is a candidate, and it is committed only after the
review recorded in [`docs/release-engineering.md`](docs/release-engineering.md)
and [`licenses/LibRaw-runtime-audit.md`](licenses/LibRaw-runtime-audit.md).
