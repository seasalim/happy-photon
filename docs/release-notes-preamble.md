Happy Photon is a focused, open-source photo workflow for RAW and JPEG
shoots. This is pre-1.0 software: keep backups and verify the formats and
cameras important to your work.

## Install

- **Windows:** install
  [Happy Photon from the Microsoft Store](https://apps.microsoft.com/detail/9N45WWF08BP8).
  The Store build is Microsoft-signed and receives updates through the Store;
  Windows ZIPs are not distributed on GitHub.
- **Linux:** download the x86_64 AppImage, make it executable, and run it, or
  download the `linux-x64` archive, extract it, and run `HappyPhoton`. The
  AppImage's pinned type-2 runtime is statically linked, so no `libfuse2`
  package is needed; it mounts itself through the FUSE support current
  distributions already ship. On a system without FUSE, run it with
  `--appimage-extract-and-run`.
- **macOS:** download the `osx-arm64` ZIP on an Apple Silicon Mac running
  macOS 14 or later. Safari may unpack it automatically; otherwise, open the
  ZIP once. Open **Happy Photon.app** and optionally move it to Applications.
  Tagged Mac artifacts are Developer ID signed and notarized.

Checksums remain available in `SHA256SUMS.txt` for people who want to verify a
download manually. The Linux and macOS archives contain the GPL license,
trademark policy, dependency inventory, and third-party notices.

## Known limitations

- Camera and RAW compatibility varies by platform and capture mode.
- HEIC/HEIF support depends on operating-system codecs.
- Linux desktop integration is limited to the AppImage's desktop entry and
  icon.

## Privacy and original-file safety

Happy Photon has no account, subscription, product telemetry, or advertising.
Original image files are never modified.

The generated changes section follows this release-specific guidance.
