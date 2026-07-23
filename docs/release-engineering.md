# Release engineering

Happy Photon has two release paths:

- A manual `Release` workflow run builds unsigned Windows/Linux archives and
  an ad-hoc-signed Mac archive for private inspection. It uploads workflow
  artifacts but never creates a GitHub Release.
- A pushed `v*` tag builds platform-native artifacts and creates a draft
  GitHub Release. Tagged Mac builds must be Developer ID signed and notarized.
  Final tags without a prerelease suffix must also be signed with Microsoft
  Artifact Signing. A prerelease tag may produce an unsigned private Windows
  candidate.

Every release artifact includes `LICENSE`, `TRADEMARKS.md`,
`THIRD_PARTY_NOTICES.md`, the `licenses/` bundle, and a generated
`DEPENDENCIES.json`. The assembly job generates `SHA256SUMS.txt`. When the
repository is public, it also creates GitHub build-provenance attestations.

## Repository variables

Set these non-secret repository variables:

- `MACOS_SIGNING_ENABLED=true` before pushing any release tag.
- `WINDOWS_SIGNING_ENABLED=true` before pushing a final release tag.
- `AZURE_ARTIFACT_SIGNING_ENDPOINT`
- `AZURE_ARTIFACT_SIGNING_ACCOUNT`
- `AZURE_ARTIFACT_SIGNING_CERTIFICATE_PROFILE`

The Azure certificate profile must use Public Trust. Its subject is the
verified publisher shown to Windows users, so configure it only after deciding
whether the publisher is the maintainer or a legal business entity.

## Repository secrets

For Microsoft Artifact Signing with GitHub OIDC:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`

Grant the federated identity only the **Artifact Signing Certificate Profile
Signer** role needed for the selected profile.

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
2. Run the `Release` workflow manually with a SemVer-compatible version.
3. Download the combined dry-run artifact and inspect the three archives,
   dependency inventories, notices, and checksums.
4. Configure and verify the required signing settings.
5. Create and push an annotated `v*` tag for the reviewed commit.
6. Download the resulting draft assets on clean machines, verify checksums,
   signatures, Gatekeeper behavior, launch, import, edit, and export.
7. Publish the draft only after the release checklist passes.

Never treat the ad-hoc-signed manual Mac build as a public release.

Official references:

- [GitHub artifact attestations](https://docs.github.com/en/actions/how-tos/secure-your-work/use-artifact-attestations)
- [Microsoft Artifact Signing action](https://github.com/Azure/artifact-signing-action)
- [Apple notarization](https://developer.apple.com/documentation/security/notarizing-macos-software-before-distribution)
