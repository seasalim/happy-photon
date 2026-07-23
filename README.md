# <img src="Assets/happy-photon-icon.png" alt="Happy Photon icon" width="48" align="absmiddle"> Happy Photon

### Photo editing, simplified. An opinionated workflow built around the few decisions that shape a photograph.

Happy Photon is an open-source desktop application for browsing, developing,
rating, and exporting RAW and JPEG shoots. Originals stay untouched, the
catalog stays local, and no account or subscription is required.

![Happy Photon Library view showing a wildlife shoot with ratings, burst
groups, histogram, and adjustments](docs/screenshots/Screenshot_Library.png)

> Happy Photon is preparing for its `v0.1.0` public preview. Release downloads
> are not published yet; build from source if you want to test the current
> development version.

## What it does

- Browse folders without importing or relocating originals.
- Develop JPEG and RAW files with non-destructive tonal, color, curve,
  rotation, horizon, and crop edits.
- Rate, flag, filter, group bursts, and select images for export.
- Create reusable presets and copy edits between images.
- Export JPEG, PNG, and WebP copies with named size variants.
- Optionally connect an agent through the PixelBlind interface.

![Happy Photon Develop view editing a bear photo with presets, histogram,
tone curve, and adjustments](docs/screenshots/Screenshot_Develop.png)

## Originals are never modified

Edits live in the Happy Photon catalog under
`~/Pictures/Happy Photon Catalog/`. Export always creates new files, and both
the UI and agent interface reject output paths that would overwrite a loaded
original.

Use a backup and a scratch copy of important shoots while testing a pre-release
build.

## PixelBlind

An optional, off-by-default, Agent toggle starts a token-protected MCP server
on localhost and copies its connection URL to the clipboard. Disabling Agent
stops access.

| Agent-visible | Blocked |
| --- | --- |
| Filenames and paths | Thumbnails and previews |
| EXIF and file metadata | Original file bytes |
| Ratings, flags, edits, burst IDs, and preset names | Rendered pixels |
| Locally computed sharpness, clipping, and luminance statistics | Arbitrary file reads |

Agent-visible data may be transmitted wherever the MCP client and model you
choose run. Agent mutations apply immediately and persist to the catalog.
`v0.1.0` does not include an activity ledger, proposal queue, or session revert.

## Supported systems

- Windows x64
- Linux x64
- macOS 14 or newer on Apple Silicon

Standard formats include JPEG, PNG, BMP, GIF, TIFF, and WebP. RAW support
includes CR2/CR3, NEF/NRW, ARW/SRF/SR2, DNG, RAF, ORF, RW2, and PEF. Actual
camera and format support depends on the platform decoder path; the public
preview will ship with a tested compatibility matrix.

HEIC/HEIF requires platform codec support. Intel macOS is not a supported
public target.

## Build from source

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

## Project

- [Architecture](docs/ARCHITECTURE.md)
- [Design guide](docs/DESIGN.md)
- [Contributing](CONTRIBUTING.md)
- [Security](SECURITY.md)
- [Trademark policy](TRADEMARKS.md)
- [Release engineering](docs/release-engineering.md)
- [Third-party notices](THIRD_PARTY_NOTICES.md)

## License

Happy Photon is licensed under
[GPL-3.0-or-later](LICENSE). Contributions are accepted under the same terms
without a contributor license agreement or copyright assignment. Third-party
components retain their own licenses.
