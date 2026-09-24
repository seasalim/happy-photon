# Lens corrections

RAW corrections use embedded prescriptions or the pinned `data/lensfun` snapshot
(identity: [THIRD_PARTY_NOTICES.md](../../THIRD_PARTY_NOTICES.md#lensfun-database)).
Exact conservative matches enable every supported class without per-lens/class
qualification. JPEG/HEIC sources are uncorrected; newly seen images default Distortion
and Chromatic aberration on, Vignetting off.

Each class resolves independently: qualified embedded data, then Lensfun, else
unavailable. Camera maker/model and lens model must match after
case/whitespace/punctuation normalization, with compatible mounts. Cameras try primary
names before English aliases. Lenses exhaust primary exact and
distinct-alphanumeric-token-set matches before the same English-alias tiers; token order
is irrelevant. Other languages do not participate. The first nonempty candidate set is
terminal; multiple matches remain ambiguous. Maker prefixes may occur in supplied or
database model identities. Missing/ambiguous interchangeable-lens identity cannot match;
fixed mounts may omit identity only with one database lens. EXIF lens identity comes
first. Without a unique match, bridge ABI 4 reads LibRaw's parsed maker-note facts at
the header stage: try transmitted name, then composite-ID-derived name if it misses.
Composite F-mount IDs require confirmed LibRaw F-mount identity and a table selected by
normalized maker. Only `data/lens-ids/nikon.tsv` ships, derived from ExifTool's
published tag documentation. Unknown IDs, missing tables, multi-name rows and duplicate
keys yield no data; there is no focal/aperture guessing or non-CPU recovery.

## Same-optics aliases and manual selection

`data/lens-ids/same-optics.tsv` maps curated source names to Lensfun models only after
EXIF, maker-note and ID-derived exact candidates miss. The same candidate order applies;
lookups are normalized, case-insensitive and single-hop. Malformed/conflicting rows
reject the table; blank/comment and self-alias rows are ignored. No focal-range guessing.

The Nikon aliases cover AF 20/2.8, 24/2.8, 35/2, 50/1.4, 50/1.8 (including N),
85/1.8, and 180/2.8 IF-ED families whose D successors retain the optical formula.
Provenance: Nikon's [24mm history](https://imaging.nikon.com/imaging/information/story/0086/)
and [50mm f/1.8 history](https://imaging.nikon.com/imaging/information/story/0060/),
MIR's [20mm history](https://www.mir.com.my/rb/photography/companies/nikon/nikkoresources/AFNikkor/AFNikkor20mmf28D/index1.htm),
MIR's [35mm comparison](https://www.mir.com.my/rb/photography/companies/nikon/nikkoresources/AFNikkor/AF35mm/index.htm)
and [85mm comparison](https://www.mir.com.my/rb/photography/companies/nikon/nikkoresources/AFNikkor/AF85mm/index1.htm),
Richard Haw's [50mm f/1.4 optical history](https://richardhaw.com/2019/05/03/repair-nikkor-50mm-f-1-4-ai-s/),
and the [180mm teardown comparison](https://phillipreeve.net/blog/nikon-af-180mm-2-8d-versions-teardown-and-repair-guide/).
The AF 28mm f/2.8 is deliberately excluded: its D successor changed optical design.
The Rokinon row is the Samyang 20mm f/1.8 ED AS UMC sold under the Rokinon brand;
see the distributor's [brand information](https://rokinon.com/pages/about-us) and
[20mm specification](https://rokinon.com/products/20mm-f1-8-full-frame-wide-angle).

Develop offers Automatic and mount-compatible logical Lensfun names, consolidating
calibration variants and ranking them as automatic matching does. Decode supplies the
picker summary; UI lookup never opens a second database. Embedded data still wins
per class after a manual selection. JPEG/HEIC and monochrome remain uncorrected.

## Placement and interpolation ledger

Corrections are decode-dependent. A corrected destination coordinate maps to the
demosaiced camera-native source separately for R, G, and B. Bilinear sampling happens
before the camera-to-Rec.2020 matrix, radial vignetting gain multiplies those sampled
scene-linear camera values, and characterization writes Q16 once. Embedded DNG and RAF
table gains use the output-geometry coordinate required by their existing contracts;
Lensfun `pa` gain uses the shared green post-geometry coordinate, where the pristine
source was sampled.

Preview imports sample directly to each requested size, replacing the normal resize;
full-resolution corrections add one warp pass. Horizon remains render-side.
One centered scale-to-cover is solved across active planes in the native logical frame,
independent of half/full sampling density. Crop stays normalized to the corrected base;
source-saturation follows the same maps with OR semantics, while RAW histogram remains
pre-warp. No extra image interpolation is introduced in preview.

## DNG subset

The reader follows Adobe's public [DNG 1.7.1 specification](https://helpx.adobe.com/content/dam/help/en/camera-raw/digital-negative/jcr_content/root/content/flex/items/position/position-par/download_section_733958301/download-1/DNG_Spec_1_7_1_0.pdf).
Payloads are big-endian. List 3 supports WarpRectilinear, FixVignetteRadial and
TrimBounds; list 2 supports only FixVignetteRadial, whose smooth gain approximately
commutes with demosaic. ActiveArea defines visible source, DefaultCrop the output unless
TrimBounds replaces it; centers remain in the DNG logical frame. Mandatory list-1
operations, list-2 warps, unknown mandatory opcodes, invalid/non-finite bounds or
unsupported planes reject the whole prescription. Optional list-1/unknown operations are
skipped.

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
| Fujifilm X, 23/31/23 generation | Pinned; each class independent | Non-identity distortion enabled; CA and vignetting deferred | Authored fixtures pin the qualified distortion subset; identity tables advertise no operation |
| Fujifilm X, 19/29/19 generation | Pinned; trailing CA scale sentinel required | Deferred per class | Parsing coverage does not establish a production correction |
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

The shipped database is a pinned manual snapshot in `data/lensfun`; there is no
runtime network access or automatic update. Database-only numeric calibration suffixes
do not change lens identity; crop-distance ranking resolves calibrations, while tied
or distinct identities remain ambiguous. Application toggles do not affect resolution.

### Evidence

Matching is conservative; unknown or ambiguous identities produce no data. Historical
qualification runs live in git history; retired per-lens gates no longer apply.
`scripts/evaluate-raf-lens-corrections.cs` is a qualification instrument whose
embedded-JPEG oracle can penalize genuinely straightening corrections.

The Nikon table follows published documentation and recorded provenance. Focal/aperture
ranges cannot independently corroborate names: LibRaw derives them from the same
composite bytes. Keys differing only in lens-ID/MCU bytes depend on the published
source's accuracy. Unknown/ambiguous keys yield no data; recovered names must still
match Lensfun. This limitation concerns Nikon F, not other makers or uncoded lenses.

## Settings, cache, and compatibility

`LensSettings` stores three required booleans: distortion and chromatic aberration
default on, vignetting off. The nullable `profileOverride` stores a per-image manual
lens selection; there is no baseline field. `HasEdits` compares with those defaults,
and Reset restores them and clears the override. Copy/paste and presets transfer only
the booleans and preserve the destination override. RENDER.md §8 owns version acceptance.

The three bits and escaped manual override join `BaseDecodeSettings.CacheKey`.
Changing them re-decodes; version values and cache invalidation rules live in code
and OVERVIEW.md §6.
