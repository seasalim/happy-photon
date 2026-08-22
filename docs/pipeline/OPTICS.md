# Embedded lens corrections

Happy Photon applies only correction prescriptions embedded in RAW files. There is no
lens-profile database, lens matching, or correction for JPEG/HEIC sources. Distortion
and Chromatic aberration default on for newly seen images; Vignetting defaults off.

## Placement and interpolation ledger

Corrections are decode-dependent. A corrected destination coordinate maps to the
demosaiced camera-native source separately for R, G, and B. Bilinear sampling happens
before the camera-to-Rec.2020 matrix, radial vignetting gain multiplies those sampled
scene-linear camera values, and characterization writes Q16 once.

For an interactive or large preview base this fused import writes the requested size
directly and replaces that base's existing resize. Each preview base therefore has one
decode interpolation whether optics are active or not. A native full base ordinarily
needs no resize, so active corrections add its one budgeted warp pass. Render-side
quarter turns, horizon rotation, crop, and final output resizing are unchanged. Horizon
remains an interactive render edit and is not folded into a decode-cached operation.

One centered scale-to-cover factor is solved across all active planes so corrected
edges stay inside the visible source. Crop coordinates remain normalized to the
corrected base. Source-saturation flags follow the same per-channel map with categorical
OR semantics; the RAW sensor histogram remains a pre-warp sensor measurement.

## DNG subset

The reader follows Adobe's public [DNG 1.7.1 specification](https://helpx.adobe.com/content/dam/help/en/camera-raw/digital-negative/jcr_content/root/content/flex/items/position/position-par/download_section_733958301/download-1/DNG_Spec_1_7_1_0.pdf).
Opcode payloads are read big-endian independent of TIFF byte order.

- OpcodeList3: `WarpRectilinear`, `FixVignetteRadial`, and `TrimBounds`.
- OpcodeList2: `FixVignetteRadial` only. Its smooth scene-linear gain field commutes
  with demosaic interpolation to negligible error; mosaic-stage geometry does not.
- ActiveArea defines LibRaw's visible source window. DefaultCrop defines the corrected
  output window unless TrimBounds replaces it. Optical centers remain in the DNG
  logical frame.
- A mandatory OpcodeList1 operation, list-2 warp, unknown mandatory opcode, invalid bounds,
  non-finite value, or unsupported plane count rejects the complete prescription.
  Optional OpcodeList1 and unknown optional opcodes are skipped; a partial correction
  is never applied.

One or three RGB `WarpRectilinear` coefficient sets are supported. Distortion-only uses
green geometry for every plane. CA-only retains red/blue differential maps relative to
inverted green geometry. Vignetting uses the specified even-power scene-linear gain.
The fused implementation evaluates vignetting at the output-geometry point rather than
reordering the gain around individual warps. For a gain field `G` and warp `W`, the
absolute gain approximation is bounded by
`sup(segment(p,W(p))) |gradient G| * |W(p)-p|`; DNG coefficients are not globally
bounded, so no smaller universal numeric bound is claimed. This preserves one sampling
pass while making the approximation explicit for files that order vignetting before a
warp.

## Fujifilm RAF subset and coverage

The RAF reader uses the header-declared raw-data TIFF and the publicly documented
[Fujifilm tag identities](https://exiftool.org/TagNames/FujiFilm.html): 0xf00b geometric
distortion, 0xf00f chromatic aberration, and 0xf010 vignetting. No exiftool, darktable,
RawTherapee, or other GPL source code was consulted. Table layout parsing was derived
empirically from Happy Photon's own committed RAF fixtures.

| Source / mount | Parsing | Application | Evidence |
|---|---|---|---|
| DNG embedded opcodes | Supported subset above | Enabled | Synthetic authored-opcode and inversion tests |
| Fujifilm X, X30 fixture | 23/31/23 tables pinned | Deferred | Private semantics have not passed the required embedded-preview alignment validator |
| Monochrome RAW sensors | Not read in v1 | Uncorrected | The v1 correction pass requires three camera-native planes |
| Sony E, Micro Four Thirds | None | None | Out of scope |
| JPEG / HEIC | None | None | RAW-only boundary |

The Fuji parser reports no applicable corrections until the in-file camera-preview
validator proves that a candidate interpretation improves alignment. The UI therefore
shows the honest no-data state even though the parse alarm protects future work.

## Settings, cache, and compatibility

EditSettings v3 always stores a `lens` block with the booleans and a `standard` or
`legacy` baseline. Reading v2 materializes all-off/legacy in memory; saving writes an
explicit v3 block, so it can never acquire defaults later. New rows use
on/on/off/standard. `HasEdits` compares with the image's baseline, Reset restores it,
and copy/paste, presets, and agent patches transfer only the booleans.

The three bits join `BaseDecodeSettings.CacheKey`. `BaseImage.Version` is 13;
`RenderPipeline.Version` stays 10 because render-stage math is unchanged.
