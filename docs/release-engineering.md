# Release engineering

Happy Photon has two coordinated release paths:

- A manual `Release` workflow run builds Windows/Linux archives and an
  ad-hoc-signed Mac archive for private inspection. A final version also
  produces an unsigned Microsoft Store MSIX as a separate private workflow
  artifact. Manual runs never create a GitHub Release.
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
   Gatekeeper behavior, launch, import, edit, and export.
7. Download the private MSIX from the tagged workflow run and upload that exact
   file manually to the Happy Photon submission in Partner Center. Complete
   the `runFullTrust` restricted-capability justification there.
8. Publish the GitHub draft and submit the Partner Center draft only after the
   release checklist passes.

Never treat the ad-hoc-signed manual Mac build as a public release.

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
