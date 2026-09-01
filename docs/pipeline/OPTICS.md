# Lens corrections

Happy Photon applies embedded correction prescriptions to RAW files and resolves
prescriptions from the pinned Lensfun database snapshot in `data/lensfun`. An exact,
conservative Lensfun match is trusted: every supported correction class in the matched
profile is available without a per-lens or per-class qualification gate. There is no
correction for JPEG/HEIC sources. Distortion and Chromatic aberration default on for
newly seen images; Vignetting defaults off.

Resolution is conservative and happens independently for each correction class:
qualified embedded data wins, Lensfun fills an unrepresented class, and otherwise the
class remains unavailable. Camera maker/model and lens model must match exactly after
case, whitespace, and punctuation normalization, and the lens mount must be compatible
with the matched camera. When no exact lens candidate exists, equality of the distinct
alphanumeric token sets may match the same words in a different order. A non-empty exact
candidate set is terminal, and multiple token matches remain ambiguous. A maker prefix
may appear in either the supplied or database model identity for both cameras and lenses.
Missing or ambiguous interchangeable-lens identity produces no match; a
fixed-lens mount may omit lens identity only when it has exactly one database lens.
The EXIF lens string is the primary identity. If it produces no unique profile, bridge
ABI 4 supplies LibRaw's already-parsed maker-note lens facts at the header stage. A
transmitted maker-note lens name is tried next, followed by the composite-ID-derived name
when the transmitted name does not match. F-mount composite IDs are resolved only for a
confirmed LibRaw F-mount identity through a table selected from the normalized maker
name. The shipped
`data/lens-ids/nikon.tsv` table is derived from ExifTool's published tag documentation;
no other maker table is currently shipped. Unknown IDs, missing tables, multi-name
rows, and duplicate-key groups remain no-data. Focal/aperture guessing and non-CPU lens
recovery are not implemented.

## Placement and interpolation ledger

Corrections are decode-dependent. A corrected destination coordinate maps to the
demosaiced camera-native source separately for R, G, and B. Bilinear sampling happens
before the camera-to-Rec.2020 matrix, radial vignetting gain multiplies those sampled
scene-linear camera values, and characterization writes Q16 once. Embedded DNG and RAF
table gains use the output-geometry coordinate required by their existing contracts;
Lensfun `pa` gain uses the shared green post-geometry coordinate, where the pristine
source was sampled.

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
| Lensfun rectilinear profiles | `poly3`, `poly5`, `ptlens` distortion; `linear`, `poly3` TCA; `pa` vignetting | Enabled for every class supplied by an exact, mount-compatible match | Formula-level synthetic oracle and full-snapshot parse tests; `acm` is unsupported |
| Monochrome RAW sensors | Not read in v1 | Uncorrected | The v1 correction pass requires three camera-native planes |
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

## Lensfun models and interpolation

Lensfun distortion and TCA radii use half the smaller calibration-sensor dimension
as one unit; Happy Photon rescales that radius to the actual sensor using the profile
crop factor and aspect ratio, the matched camera crop factor, and the decoded
visible-frame aspect. PA vignetting radii instead use half the calibration-sensor
diagonal as one unit, so their sensor rescale is the pure crop-factor ratio (verified
differentially against the reference library). The database optical-center offsets
use the smaller-dimension convention. Warp
evaluation follows the documented destination-to-source sequence: shared distortion
first, then per-channel lateral CA. The PA vignetting model contributes the reciprocal
scene-linear gain at the shared green post-geometry coordinate in the same fused
pre-matrix pass.

For distortion and TCA, coefficients interpolate linearly in log focal length when
the bracketing entries use the same model. A model-family boundary selects the nearest
entry instead. Values clamp at the calibrated range edges. Vignetting first selects
the largest calibrated focus distance (an infinity assumption because LibRaw does not
provide focus distance), then interpolates over aperture and log focal length with the
same edge clamping. `acm` calibrations produce no data in this version.

The shipped database is a manual snapshot of Lensfun git master at commit `1c8b8f0`.
There is no runtime network access or automatic update path.

An exact or distinct-token-set, mount-compatible match trusts the database and exposes
every supported, non-identity class in the matched profile. Token order and repeated
tokens do not affect distinct-token-set equality. There is no production pin table or
instrument-evidence gate. Matching remains deliberately conservative: missing or
ambiguous identity still produces no data. A Lensfun model may carry a trailing integer
calibration token absent from the supplied identity; that database-only suffix is
ignored for name equality, after which multiple calibrations still use the existing
crop-distance ranking and tied or distinct identities remain ambiguous. `ForceSource`
bypasses embedded readers for qualification. Resolution is independent of the application toggles, which gate
application only; whenever an embedded prescription leaves any class unfilled, Lensfun
is consulted. The first Lensfun resolution pays the measured one-time 162.7 ms parse
cost and retains 6.7 MB before matching determines whether a profile applies.

`scripts/evaluate-raf-lens-corrections.cs` can force either the embedded or Lensfun
source. It selects a camera JPEG with a long edge of at
least 1024 px and aspect within 2% of the oriented visible frame, preferring pixel count
then file offset and recording offset, dimensions, and SHA-256. It compares isolated
class ablations after global registration on disjoint grid halves and requires the
class-specific reduction plus an improvement above a bootstrap 3σ floor. It is a
developer qualification instrument, never a decode-time validator.

The 2026-08-23 committed-fixture run found one conservative Lensfun match among seven
RAW fixtures: the fixed-lens X30. No wrong-lens match was accepted. Forced Lensfun
distortion reduced its aggregate displacement residual 40.7%, compared with the
embedded prescription's qualified 61.0%, but one split-grid half missed the 3σ floor.
Lensfun CA reduced the residual 3.5% and also failed; vignetting was an identity
operation for this profile. Under the now-retired qualification policy these results
enabled no Lensfun class. They remain informational evidence, not production gates.
The committed 6D reports only `8mm`; multiple Canon-EF 8 mm database lenses make that
identity ambiguous, so the matcher correctly returns no data.

The 2026-08-24 maker-note identity gate swept 10,778 local Nikon files. Every file
carried a composite lens ID and every one resolved, across eight distinct IDs, with no
unknown or ambiguous key; no file was skipped as unavailable. Of those files 8,952
(83.1%) reached a Lensfun profile across six lenses. The remaining two identities stay
no-data honestly: the database carries no profile for the 85 mm f/1.4D, and the one
third-party lens present has none either.

What that verification does and does not establish is worth stating precisely. The
shipped table is a byte-faithful transcription of the published documentation, checked
by hashing both the retrieved source and the generated table against the recorded
provenance, and every resolved name is consistent with the focal length, maximum
aperture, and lens-feature bits its own key encodes. It is not an independent check of
the name itself: LibRaw derives those focal and aperture values from bytes 2 to 5 of the
same composite, so a key and its range facts cannot corroborate each other. Keys that
differ only in the lens-ID and MCU bytes are indistinguishable this way -- 321 of the 602
resolvable published keys share their range and feature bytes with another entry, and two
of the eight identities seen locally are such a pair. For those, correctness rests on the
published documentation being right, which is this feature's declared source of truth.
The safety boundary is unchanged and sits elsewhere: an unknown or ambiguous key still
produces no data, and a recovered name still has to match a database lens exactly.

This is Nikon F evidence and says nothing about other makers or uncoded lenses.

The subsequent 21,823-file library sweep accepted three camera/lens identities with zero
wrong-lens matches: Canon PowerShot G11, XF27mmF2.8 R WR, and
XF16-50mmF2.8-4.8 R LM WR. G11 distortion increased residual on every evaluable sample;
XF27 distortion passed zero of five samples. Across the five-file samples for both Fuji
lenses, every CA ablation failed and increased its class residual. The original
vignetting measurements predated the corner-normalization fix and measured a
systematically over-strong correction; re-measured after the fix, XF16-50 vignetting
is mixed and mild (8 of 13 samples improved, up to 33.7%, one passing its individual
gate; 5 mildly worse within the tone-curve confound). The
expanded XF16-50 distortion G5 retained a 55.9% median improvement and harmed no
evaluable file beyond its 3σ floor, though only 6 of 13 evaluable samples
cleared the per-file split-grid gate. After visual A/B review of every
evaluable sample, the user ruled the individual-pass criterion too strict for this case
and accepted XF16-50 distortion under the then-current G5 gate. The later
trust-the-database ruling retired G5 and all per-lens/class gates. The G11, XF27, and
XF16-50 measurements are retained as history only; profiles they scored against now
apply when exactly matched and their class toggle is on. Interpret the negative scores
with care: the embedded-JPEG oracle measures agreement with the manufacturer's
rendering, so on cameras whose JPEGs apply little or no distortion correction (the G11
review case) it penalizes genuinely straightening corrections; user visual review of
the G11 samples found the corrected geometry fine.

## Settings, cache, and compatibility

EditSettings v3 always stores a `lens` block with the booleans and a `standard` or
`legacy` baseline. Reading v2 materializes all-off/legacy in memory; saving writes an
explicit v3 block, so it can never acquire defaults later. New rows use
on/on/off/standard. `HasEdits` compares with the image's baseline, Reset restores it,
and copy/paste and presets transfer only the booleans.

The three bits join `BaseDecodeSettings.CacheKey`. `BaseImage.Version` is 18 because
order-tolerant identity and the ID-derived fallback change which files decode with
corrections applied.
`RenderPipeline.Version` is 12, unchanged by lens identity because render-stage math
is untouched.
