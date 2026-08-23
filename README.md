# <img src="Assets/happy-photon-icon.png" alt="Happy Photon icon" width="48" align="absmiddle"> Happy Photon - Photo Editing, Simplified.

### A focused workflow backed by a serious photographic pipeline.

Happy Photon is an open-source desktop application for browsing, developing, and
exporting your photographs. It keeps the workflow approachable without asking you to
give up wide-gamut color, precise RAW development, or high-quality delivery.

It was originally built for an audience of one to solve a specific use case:
a friendly and easy to use application that reduces photo editing overhead to a minimum, and does not require paying any fees.

Photographers who are ready to graduate from complex workflows and regular users who
never fell into the complexity trap in the first place may appreciate Happy Photon's
ethos.

Always: originals stay untouched, the catalog stays local, and no account or
subscription is required.

![Happy Photon Library view showing a wildlife shoot with ratings, burst
groups, histogram, and review metadata](docs/screenshots/Screenshot_Library.png)

## The Happy Photon workflow

A fast and easy workflow separates a shoot into three decisions:

1. **Which photographs are worth keeping?** Browse your downloaded photos in place,
   and use flags, ratings, and filters to narrow the shoot.
2. **What should each keeper look like?** Shape composition, light, color, tone,
   detail, and finishing effects with precise global, non-destructive controls.
3. **What output do you need?** Select the finished photographs and export the
   required size, format, color space, and sharpening without changing the originals.

[Follow the complete workflow, from opening a shoot to exporting it](docs/WORKFLOW.md).

![Happy Photon Develop view editing a bear photo with presets, histogram,
tone curve, and adjustments](docs/screenshots/Screenshot_Develop.png)

## Pro-level processing, Happy Photon simplicity

Happy Photon pairs its fast, three-decision workflow with a deep image engine.

- **Wide-gamut from input to output.** Images are developed in a 16-bit linear
  Rec.2020 working space, with perceptual OKLCh color processing and color-managed
  export to sRGB or Display P3.
- **A RAW pipeline built for real cameras.** Scene-referred AgX tone rendering,
  measured as-shot white balance, highlight reconstruction, DCP camera profiles,
  true monochrome RAW support, and embedded RAW lens corrections where supported.
- **Advanced color and tone controls.** Kelvin and tint, Auto and eyedropper white
  balance, exposure, highlights, shadows, contrast, saturation, skin-aware vibrance,
  an eight-band HSL color mixer, and composite plus per-channel RGB tone curves.
- **Detail and finishing tools.** Capture sharpening, chroma and RAW noise reduction,
  crop and horizon straightening, vignette, deterministic film grain, and independent
  screen or print output sharpening.
- **Scopes that help you make decisions.** Display histogram, luminance waveform, RAW
  sensor histogram, source-highlight and display-floor clipping overlays, device-true
  zoom, before/after, and an invariant mid-gray assessment surround.
- **Professional handoff without a detour.** Export JPEG, PNG, WebP, or lossless
  16-bit TIFF with embedded ICC profiles, normalized EXIF, optional GPS stripping,
  collision protection, and the same rendering pipeline used by the preview.

Every edit remains non-destructive and is saved automatically. Undo and redo, personal
presets, hover previews, and copy/paste across a series make the advanced controls
practical for an entire shoot rather than just one hero frame.

## Supported systems

- Windows x64
- Linux x64
- macOS 14 or newer on Apple Silicon

Standard formats include JPEG, PNG, BMP, GIF, TIFF, and WebP. RAW support
is verified for CR2/CR3, NEF, ARW, DNG, RAF, and RW2. NRW, ORF, and PEF
open through the same decoder but are not part of the verified set. RAW
decoding uses the bundled, audited [LibRaw 0.22.2](https://www.libraw.org/news/libraw-0-22-2-release)
generation, so a listed extension does not guarantee support
for every camera model or compression variant—especially newer bodies. The
in-app workflow provides global photographic adjustments and supports embedded lens
correction prescriptions for a qualified subset of DNG and Fujifilm RAF files. It does
not currently include local masks, perspective correction, database-matched lens
profiles, layer compositing, HDR output, or custom output color profiles.

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
