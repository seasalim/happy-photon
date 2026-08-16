# <img src="Assets/happy-photon-icon.png" alt="Happy Photon icon" width="48" align="absmiddle"> Happy Photon - Photo Editing, Simplified.

### An opinionated workflow built around just three decisions.

Happy Photon is an open-source desktop application for browsing, developing, and exporting your photographs.

It was originally built for an audience of one to solve a specific use case:
a friendly and easy to use application that reduces photo editing overhead to a minimum, and does not require paying any fees.

Photographers who are ready to graduate from complex workflows and regular users who never fell into the complexity trap in the first place may appreciate Happy Photon's ethos.

Always - originals stay untouched, the catalog stays local, and no account or subscription is required.

![Happy Photon Library view showing a wildlife shoot with ratings, burst
groups, histogram, and adjustments](docs/screenshots/Screenshot_Library.png)

## The Happy Photon workflow

A fast and easy workflow separates a shoot into three decisions:

1. **Which photographs are worth keeping?** Browse your downloaded photos in place,
   and use flags, ratings, and filters to narrow the shoot.
2. **What should each keeper look like?** Crop and shape composition, and adjust light, color, and
   tone with a focused set of global, non-destructive adjustments.
3. **What output do you need?** Select the finished photographs and export the
   required sizes and formats without changing the originals.

[Follow the complete workflow, from opening a shoot to exporting it](docs/WORKFLOW.md).

![Happy Photon Develop view editing a bear photo with presets, histogram,
tone curve, and adjustments](docs/screenshots/Screenshot_Develop.png)

## PixelBlind (Optional / experimental)

Happy Photon lets you experiment with an optional, off-by-default agentic AI.

An Agent toggle starts a local MCP server that you can then use your
favorite agent to converse and cull your images.

**No images are ever sent to the model - thus, the Agent stays blind to the actual pixels**

[![PixelBlind finding images with clipped highlights and marking them as
rejected in Happy Photon](docs/screenshots/PixelBlind_Demo.webp)](docs/screenshots/Screen%20Recording_PixelBlind.mp4)

| Agent-visible | Blocked |
| --- | --- |
| Filenames and paths | Thumbnails and previews |
| EXIF and file metadata | Original file bytes |
| Ratings, flags, edits, burst IDs, and preset names | Rendered pixels |
| Locally computed sharpness, clipping, and luminance statistics | Arbitrary file reads |

Agent-visible data may be transmitted wherever the MCP client and model you choose run.

## Supported systems

- Windows x64
- Linux x64
- macOS 14 or newer on Apple Silicon

Standard formats include JPEG, PNG, BMP, GIF, TIFF, and WebP. RAW support
includes CR2/CR3, NEF/NRW, ARW/SRF/SR2, DNG, RAF, ORF, RW2, and PEF. RAW
decoding uses the bundled, audited [LibRaw 0.22.2](https://www.libraw.org/news/libraw-0-22-2-release)
generation, so a listed extension does not guarantee support
for every camera model or compression variant—especially newer bodies. The
in-app workflow provides global photographic adjustments; it does not currently
include local masks, lens or perspective correction, layer compositing, HDR
output, or custom output color profiles.

HEIC/HEIF read support is probed at runtime and can vary with the bundled codec.
Intel macOS is not a supported public target.

## Project

- [Build from source](BUILDING.md)
- [Workflow guide](docs/WORKFLOW.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Image pipeline](docs/pipeline/OVERVIEW.md)
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
