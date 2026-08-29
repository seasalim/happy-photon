# Release engineering

Happy Photon has two coordinated release paths:

- A manual `Release` workflow run builds Windows/Linux archives, a Linux
  AppImage, and an ad-hoc-signed Mac archive for private inspection. A final
  version also produces an unsigned Microsoft Store MSIX as a separate private
  workflow artifact. Manual runs never create a GitHub Release.
- A pushed `v*` tag builds platform-native artifacts and creates a draft
  GitHub Release for macOS and Linux. Tagged Mac builds must be Developer ID
  signed and notarized. For a final tag, Windows is distributed through the
  Microsoft Store: the workflow retains the unsigned MSIX privately for manual
  Partner Center upload and does not publish a Windows ZIP on GitHub.

Every release artifact includes `LICENSE`, `TRADEMARKS.md`,
`THIRD_PARTY_NOTICES.md`, the `licenses/` bundle, and a generated
`DEPENDENCIES.json`. The assembly job generates `SHA256SUMS.txt` only for the
public GitHub assets. When the repository is public, it also creates GitHub
build-provenance attestations for those assets. Partner Center is the source
of the Microsoft-signed Windows package.

macOS packaging keeps Mach-O files under `Contents/MacOS` and relocates every
other publish-output file to `Contents/Resources`, preserving relative paths.
Before signing, packaging verifies that `Contents/MacOS` contains exactly the
Mach-O path set captured before relocation and no directories. It also verifies
that `Contents/Resources/data` exists and every immediate data directory is
non-empty, with both `data/lensfun` and `data/lens-ids` required by their
runtime consumers. Bundled data resolves from `Contents/Resources` when the
application base directory is structurally `Contents/MacOS`; Windows, Linux,
and unbundled macOS builds continue to resolve it next to the binary.

The Linux job wraps its retained `linux-x64` publish output in both the
reproducible tar archive and an AppImage. AppImage packaging pins appimagetool
1.9.1 and type-2 runtime 20251108; both downloads use fixed release tags and
committed SHA-256 checks before execution. The runtime is supplied explicitly
because appimagetool otherwise downloads its latest runtime. Unlike the Linux
tar, the AppImage is not claimed to be byte-reproducible (decision recorded
2026-08-27). Its checksums and build-provenance attestation are its integrity
story.

## Build identity

The release workflow's `prepare` job checks out the requested source and
resolves the full revision plus its commit timestamp once. The timestamp is
normalized to UTC. Windows ZIP, Windows MSIX, and Linux publish receive those
shared values as `SourceRevisionId`, `SourceRevision`, and `BuildTimestampUtc`;
the macOS job passes the same values through
`HAPPY_PHOTON_SOURCE_REVISION` and `HAPPY_PHOTON_BUILD_TIMESTAMP` to
`scripts/package-macos.sh`.

This makes all artifacts from one workflow identify the same source. The
commit timestamp, rather than workflow wall-clock time, also preserves the
deterministic Linux archive design when the same commit is rebuilt. Ordinary
local builds and the release workflow's pre-publish tests intentionally remain
unstamped.

CI resolves the revision and commit timestamp once per platform job before its
retained publish. The earlier build and `dotnet test --no-build` use unstamped
inputs, so the stamped publish may recompile the application project. This is
expected.

Release versions may be final (`0.1.0`) or include a prerelease suffix
(`0.2.0-beta.1`). Caller-supplied SemVer build metadata such as `0.1.0+local`
is rejected because the SDK appends the source revision to the assembly's
informational version.

## Repository variables

Set these non-secret repository variables:

- `MACOS_SIGNING_ENABLED=true` before pushing any release tag.

## Repository secrets

For Apple Developer ID signing and App Store Connect API-key notarization:

- `APPLE_DEVELOPER_ID_CERTIFICATE_P12_BASE64`
- `APPLE_DEVELOPER_ID_CERTIFICATE_PASSWORD`
- `APPLE_DEVELOPER_ID_APPLICATION`
- `APPLE_NOTARY_KEY_ID`
- `APPLE_NOTARY_ISSUER_ID`
- `APPLE_NOTARY_KEY_P8_BASE64`

The certificate and API key are decoded only into the hosted runner's
temporary directory. The workflow uses a temporary keychain and deletes it
even when the job fails.

Developer ID signing enables Apple's hardened runtime. Because Happy Photon is
published as a self-contained .NET single-file app rather than Native AOT, the
outer app signature must include the `com.apple.security.cs.allow-jit`
entitlement from `Platforms/macOS/HappyPhoton.entitlements`. Without it,
Gatekeeper can accept the notarized bundle while CoreCLR still fails at launch.

## LibRaw native candidates

The manual-only `build-libraw.yml` workflow builds audited LibRaw and bridge
candidates; it never commits or integrates them. GitHub dispatch requires the
same-path no-op placeholder on the default branch. Run the build branch's
full workflow explicitly:

```bash
gh workflow run build-libraw.yml --ref <build branch>
```

The checkout and provenance retain the exact feature-branch commit. Candidate
version `0.22.2.N` takes `N` from the immutable `github.run_number`, so every
dispatch consumes its revision whether it succeeds or fails. Attempts other
than 1 are rejected; use a fresh dispatch instead of rerunning. Preflight also
rejects an existing committed package or candidate artifact with that version.
Version `0.22.2.0` is reserved for developer-local builds and is never a
distributable candidate.

The run uploads the isolated `baseline-0211-{rid}` logs and, after successful
gates, `libraw-{rid}-{version}` directories containing `runtime/`,
`validation/`, `performance/`, licenses, staging inventory, build options, and
`provenance.json`. A failed RID uploads the available validation/performance
files as `diagnostics-{rid}-{version}`. Assembly uploads
`libraw-candidate-{version}` with the multi-RID nupkg, combined native
provenance, and their SHA-256 summary.

Validation failures, native-test/sanitizer failures, contract mismatches, and
a repeatable native peak-memory increase above 10% are fatal. Elapsed changes
remain measured; a repeatable increase above 10% produces
`accepted-elapsed-flagged` without failing CI, and a maintainer must rule on
that flag during candidate review and again during release qualification.
Workflow artifacts are candidates, never releases: a maintainer downloads a
run's three RID sets and independently verifies contents, provenance, hashes,
and validation/performance evidence, and only after that review is the
package committed or integrated.

## Creating a candidate

1. Run CI on the intended commit and review all three platform jobs.
2. Run the `Release` workflow manually with the intended final version, such
   as `0.1.0`. Prerelease versions do not produce a Store MSIX.
3. Download and inspect both private artifacts: the combined dry run and
   `microsoft-store-msix-win-x64`. The Store artifact is retained for 14 days
   and is deliberately excluded from the combined artifact.
4. Verify the MSIX identity, version, dependency inventory, notices, and
   payload. If the reviewed source changed, run WACK again on the rebuilt
   package before submission.
5. Create and push an annotated `v*` tag for the reviewed commit.
6. Review the draft GitHub assets on clean machines, including checksums,
   Gatekeeper behavior, launch, import (including a RAW file, which proves
   the packaged native LibRaw runtime loads and decodes), edit, and export.
7. Download the private MSIX from the tagged workflow run and upload that exact
   file manually to the Happy Photon submission in Partner Center. Complete
   the `runFullTrust` restricted-capability justification there.
8. Publish the GitHub draft and submit the Partner Center draft only after the
   release checklist passes.

Never treat the ad-hoc-signed manual Mac build as a public release.

## Starting the next development line

After the release is complete and its certified Microsoft Store version is live
on the website, optionally start the next development line in a separate commit.
Advance both `HappyPhoton.csproj` and the manual `Release` workflow default to
the same `<next-version>-dev.1` value. Verify the resolved project version and
the full solution tests without changing the published tag or release assets.

## Microsoft Store package

Generate the committed Store assets after changing the application icon:

```powershell
./scripts/generate-windows-msix-assets.ps1
```

The generator preserves and verifies transparent icon corners. It also writes
`packaging/windows/StoreListing/AppTileIcon.png`; upload that file as the
300-by-300 **1:1 App tile icon** in the Partner Center Store listing so the
Store uses it instead of deriving a listing icon from the package.

Build and validate the x64 Store package with a final SemVer version:

```powershell
./scripts/package-windows-msix.ps1 -Version 0.1.0
```

The command publishes a self-contained multi-file Windows build, writes the
dependency inventory, stages the Store manifest and assets, and uses the
Windows SDK `MakeAppx.exe` tool to create and unpack-validate the package. Its
outputs are under `artifacts/windows-msix/`; they are not public release assets.
The retained MSIX is unsigned because Microsoft applies the public signature
after Store certification. It is a private workflow artifact and is not
attached to the GitHub Release. Partner Center submission remains manual. For
local testing, register the loose staged package or use an ephemeral
development certificate that is never committed.

Loose registration requires Windows Developer Mode or an equivalent sideloading
policy. After enabling it deliberately in Windows Settings, register the staged
layout for the current user:

```powershell
Add-AppxPackage -Register `
  (Resolve-Path artifacts/windows-msix/staging/AppxManifest.xml)
```

The loose package points directly at the staging directory. Close Happy Photon
and unregister it before rebuilding or deleting that directory:

```powershell
Get-AppxPackage -Name seasalim.HappyPhoton | Remove-AppxPackage
```

Do not enable Developer Mode automatically from a build script. A machine with
the default policy rejects unsigned loose registration with `0x80073CFF`; this
does not indicate a package validation failure.

Official references:

- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations)
- [Apple notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)

## Website

GitHub Pages deploys the site from `site/` via `.github/workflows/pages.yml`
(`scripts/build-site.ps1` + `scripts/check-site.ps1`). The committed webp
screenshots under `site/assets/images/` are regenerated from
`docs/screenshots/` with `dotnet run --file scripts/generate-site-images.cs`
when a screenshot changes; Pages builds never run the app.
