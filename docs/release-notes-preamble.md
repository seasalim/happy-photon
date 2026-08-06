Happy Photon is a focused, open-source photo workflow for RAW and JPEG
shoots. This is pre-1.0 software: keep backups and verify the formats and
cameras important to your work.

## Install

- **Windows:** download the `win-x64` ZIP, verify `HappyPhoton.exe`, extract
  the archive, and run it. Final public builds must show the configured
  verified publisher; a private prerelease candidate may be unsigned.
- **Linux:** download the `linux-x64` archive, extract it, and run
  `HappyPhoton`. This is a portable preview rather than a native package.
- **macOS:** download the `osx-arm64` ZIP on an Apple Silicon Mac running
  macOS 14 or later, extract it, and move **Happy Photon.app** to
  Applications. Tagged Mac artifacts are Developer ID signed and notarized.

Compare every download against `SHA256SUMS.txt`. Each application archive
contains the GPL license, trademark policy, dependency inventory, and
third-party notices.

## Known limitations

- Camera and RAW compatibility varies by platform and capture mode.
- HEIC/HEIF support depends on operating-system codecs.
- Linux desktop integration is not yet a native package.
- Agent mutations are immediate and persistent in v0.1.0; there is no
  activity ledger or session-wide revert.

## Privacy and original-file safety

Happy Photon has no account, subscription, product telemetry, or advertising.
Original image files are never modified. PixelBlind agent access is disabled
by default and exposes metadata plus local thumbnail-derived statistics, not
image pixels. A connected external agent client may transmit the returned
data under that client's own privacy terms.

The generated changes section follows this release-specific guidance.
