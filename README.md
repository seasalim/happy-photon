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

![Happy Photon Browse view showing a wildlife shoot with the filter bar, flags,
ratings, color labels, histogram, and capture metadata](docs/screenshots/Screenshot_Browse.png)

## The Happy Photon workflow

A fast and easy workflow separates a shoot into three decisions, and the application
into three matching workspaces: **Browse**, **Develop**, and **Export**.

1. **Which photographs are worth keeping?** Browse your downloaded photos in place,
   compare candidates side by side, and use flags, ratings, and filters to narrow the
   shoot.
2. **What should each keeper look like?** Shape composition, light, color, tone,
   detail, and finishing effects with non-destructive global controls and local masks.
3. **What output do you need?** Select the finished photographs and export the
   required size, format, color space, and sharpening without changing the originals.

[Follow the complete workflow, from opening a shoot to exporting it](docs/WORKFLOW.md).

![Happy Photon Develop view editing a bear photo with presets, histogram, white
balance, adjustments, and tone curve](docs/screenshots/Screenshot_Develop.png)

## New in 0.2.5

- **Adjust the parts that need it.** Draw a linear gradient across a sky or place a
  radial mask around a subject. Change Exposure, Temperature, Tint, and Saturation
  within each mask, and use feathering for smooth transitions.
- **Place masks precisely.** Drag controls on the photograph or enter numeric
  geometry. Radial masks can affect the inside or outside of the ellipse; centered
  placement gives you a quick starting point.
- **Keep editing tools close.** Crop and Locals sit beneath the histogram, with
  focused controls for creating, selecting, and adjusting masks.
- **A Windows download fallback.** The Microsoft Store remains recommended, with an
  unsigned Windows x64 ZIP available from [GitHub Releases](https://github.com/seasalim/happy-photon/releases)
  for direct download and manual updates.

Every keyboard shortcut and gesture also has a visible control, so nothing is hidden
behind a key you have to know.

## Pro-level processing, Happy Photon simplicity

Happy Photon pairs its fast, three-decision workflow with a deep image engine.

- **Bring your decisions with you.** Import ratings, flags, color labels, and
  supported crops from Lightroom Classic. Opt-in XMP sidecars keep those decisions
  portable without rewriting original pixels.
- **Cull at full size.** Browse Loupe keeps the folder tree and assessment controls
  close while you navigate, compare, and move into Develop. Lightroom-familiar
  shortcuts, dense thumbnail rows, monochrome controls, and overlay scrollbars
  keep attention on the photographs.
- **Treat a capture as one decision.** J+R groups same-name RAW and JPEG files for
  shared flags, ratings, and labels; J|R switches between them in Develop without
  losing your viewport. Compare two to four photographs in sync, and keep up to
  eight independent, labeled versions of each file for editing and export.
- **Wide-gamut from input to output.** Images are developed in a 16-bit linear
  Rec.2020 working space, with perceptual OKLCh color processing and color-managed
  export to sRGB or Display P3. Windows previews follow the active monitor profile;
  macOS previews carry sRGB intent into the system compositor.
- **A RAW pipeline built for real cameras.** Scene-referred AgX tone rendering,
  measured as-shot white balance, highlight reconstruction, DCP camera profiles,
  true monochrome RAW support, and lens corrections from embedded prescriptions or
  the bundled Lensfun database, including distortion, chromatic aberration, and
  vignetting corrections. Nikon lens identities are recovered from maker notes.
- **Advanced color and tone controls.** Kelvin and tint, Auto and eyedropper white
  balance, exposure, highlights, shadows, contrast, saturation, skin-aware vibrance,
  an eight-band HSL color mixer, and composite plus per-channel RGB tone curves.
- **Local light and color.** Combine linear and radial masks to brighten a face,
  darken a sky, or adjust warmth and color in part of the frame. Mask edits stay
  non-destructive, with undo/redo and the same adjustments carried into export.
- **Detail and finishing tools.** Capture sharpening that responds at Fit, wavelet
  luminance and chroma denoising, RAW noise reduction, crop, horizon straightening,
  vertical and horizontal perspective correction with an alignment grid, vignette,
  deterministic film grain, and independent screen or print output sharpening.
- **Scopes that help you make decisions.** Display histogram, luminance waveform, RAW
  sensor histogram, source-highlight and display-floor clipping overlays, device-true
  zoom, press-and-hold loupe, synchronized compare and before/after, and an invariant
  mid-gray assessment surround. Fit views never enlarge a photograph beyond 1:1.
- **A Develop history built for exploration.** Revisit committed adjustments,
  rotation, horizon, and crops in a persistent history. Jump to a step, preview it
  in the Navigator, undo and redo, or clear the steps above a chosen point.
- **Professional handoff without a detour.** Export JPEG, PNG, WebP, or lossless
  16-bit TIFF with embedded ICC profiles, normalized EXIF, optional GPS stripping,
  collision protection, and the same rendering pipeline used by the preview.
  A dedicated Export workspace proofs the output pixels, keeps selected captures
  in a filmstrip, and runs the queue while you continue working.

Every edit remains non-destructive and is saved automatically. Undo and redo, personal
presets, versions, hover previews, and copy/paste across a series make the advanced
controls practical for an entire shoot rather than just one hero frame.

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
in-app workflow provides global adjustments plus linear and radial local masks. Lens corrections apply to
RAW files only, from embedded prescriptions in a qualified subset of DNG and Fujifilm
RAF files or from an exact camera and lens match in the bundled Lensfun database. It
does not currently include layer compositing, HDR output, or custom
output color profiles.

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
