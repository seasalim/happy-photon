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

One centered scale-to-cover factor is solved across all active planes in the native
full-resolution logical frame so corrected edges stay inside the visible source. That
factor and frame are invariant across half/full decode scales; only the destination
sampling density changes. Crop coordinates remain normalized to the corrected base.
Source-saturation flags follow the same per-channel map with categorical OR semantics;
the RAW sensor histogram remains a pre-warp sensor measurement.

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
| Fujifilm X, 23/31/23 generation | Pinned; each class independent | Non-identity distortion enabled; CA and vignetting deferred | X30 distortion reduced registered preview displacement residual 61.0%, above both split-grid 3σ floors; X100 distortion tables were identity operations |
| Fujifilm X, 19/29/19 generation | Pinned; trailing CA scale sentinel required | Deferred per class | X-T5 corpus candidates did not pass the per-file alignment gates |
| Monochrome RAW sensors | Not read in v1 | Uncorrected | The v1 correction pass requires three camera-native planes |
| Sony E, Micro Four Thirds | None | None | Out of scope |
| JPEG / HEIC | None | None | RAW-only boundary |

Fujifilm distortion knots are interpolated without a polynomial fit. Their values are
empirically qualified as scaled radial source offsets: at a knot, source radius is
`destination radius * (1 + value / 45)`. The scale and knot count normalize the table's
radius coordinate in RAF table units. G1 qualifies one radius unit as 1.9 native visible
pixels for the 23/31/23 generation; the processor converts that unit before lookup so
LibRaw's half/full decode choice cannot change correction strength. Exact-zero
distortion tables are identity prescriptions and do not advertise an operation.
Candidate CA and vignetting interpretations remain available to the qualification
instrument but do not advertise production capabilities.

`scripts/evaluate-raf-lens-corrections.cs` selects a camera JPEG with a long edge of at
least 1024 px and aspect within 2% of the oriented visible frame, preferring pixel count
then file offset and recording offset, dimensions, and SHA-256. It compares isolated
class ablations after global registration on disjoint grid halves and requires the
class-specific reduction plus an improvement above a bootstrap 3σ floor. It is a
developer qualification instrument, never a decode-time validator.

## Settings, cache, and compatibility

EditSettings v3 always stores a `lens` block with the booleans and a `standard` or
`legacy` baseline. Reading v2 materializes all-off/legacy in memory; saving writes an
explicit v3 block, so it can never acquire defaults later. New rows use
on/on/off/standard. `HasEdits` compares with the image's baseline, Reset restores it,
and copy/paste and presets transfer only the booleans.

The three bits join `BaseDecodeSettings.CacheKey`. `BaseImage.Version` is 14;
`RenderPipeline.Version` stays 10 because render-stage math is unchanged.
