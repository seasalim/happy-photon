# Characterization: Camera RGB → Working Space

The camera-RGB convergence seam and its characterization into linear Rec.2020
D65. This document is the contract and clean-room provenance record for NEWRAW
stage 3, covering both R5a (convergence + built-in characterization, this run)
and R5b (DCP consumption, follow-up run). Sources consulted: the LibRaw 0.22.2
source distribution (LGPL-2.1/CDDL — the audited dependency this pipeline
ships; cited by file:line below), LibRaw's public API documentation, the Adobe
DNG 1.7.1 specification, and the Bradford adaptation already normative in
WHITE_BALANCE.md. The GPL trees (darktable, RawTherapee, Blender) were never
consulted.

**Status: APPROVED 2026-08-19 (NEWRAW R5a checkpoint A, run 158). §6 records
the ballot outcomes.**

## 1. The convergence seam

One linear camera-native RGB contract after LibRaw processing, for every CFA
family. The sole LibRaw parameter change from the shipped pipeline is
`output_color` 8 → 0 in the new `LinearCameraNative` configuration.
`use_camera_matrix` remains 1 and every other parameter is unchanged, so all
open-time behavior — camera-fact population, DNG embedded-matrix reading — is
bit-identical to today by construction. `output_color` is consulted only
inside `dcraw_process`/`convert_to_rgb`
(`src/postprocessing/postprocessing_utils_dcrdefs.cpp:55` sets `raw_color`
when `output_color < 1`, which skips the `out_cam` matrix application at
`postprocessing_utils.cpp:99-118`); the post-unpack configuration seam is
unchanged and correct (ruled FINAL, run 158 plan review).

The seam's samples are therefore: LibRaw's processed output after black
subtraction, white-balance scaling, demosaic (AHD Bayer / Markesteijn
X-Trans), and highlight handling — with **no** output-space conversion, no
tone curve, no auto-brightening, 16-bit linear (`gamma 1/1`,
`no_auto_bright`), exactly as today minus the final matrix.

### 1.1 Neutralization and normalization state (verified in LibRaw source)

All of the following run upstream of `convert_to_rgb` and are therefore
**identical between `output_color=8` (today) and `output_color=0`**
(`dcraw_process.cpp:90,120,127,221,254` fixes the order: `adjust_maximum` →
`scale_colors` → demosaic → `blend_highlights` → `convert_to_rgb`):

- **White balance** (`scale_colors`,
  `postprocessing_utils_dcrdefs.cpp:111-212`): with `use_camera_wb=1` and a
  valid `cam_mul`, the as-shot multipliers neutralize the as-shot illuminant —
  as-shot neutral is (1,1,1) in the seam. When `cam_mul` is missing/invalid,
  LibRaw falls back to auto-WB from image statistics (lines 123-160) — a
  pre-existing, image-dependent carve-out to the pipeline's no-auto invariant,
  unchanged by this stage. LibRaw 0.22.2 offers a deterministic daylight
  fallback (`LIBRAW_RAWOPTIONS_CAMERAWB_FALLBACK_TO_DAYLIGHT`, line 126) that
  the bridge does not currently expose; adopting it is a possible future pin,
  not part of R5a.
- **Normalization** (lines 199-212): `maximum -= black`, then
  `scale_mul[c] = (pre_mul[c]/dmax) · 65535/maximum`, where `dmax` is the
  **minimum** of `pre_mul` under highlight mode 0 (Clip) — so all channels can
  reach saturation together — and the maximum under Blend. Scaling is
  therefore WB- and highlight-mode-dependent, exactly as today.
  `adjust_maximum` (`dcraw_process.cpp:90`) tunes `maximum` from the frame's
  measured data maximum (threshold `adjust_maximum_thr`), image-dependent,
  also exactly as today.
- **Highlight handling**: Clip (mode 0) happens via the scaling above;
  Blend (mode 2) runs post-demosaic in camera space
  (`dcraw_process.cpp:221`), upstream of the seam. Both decode modes survive
  R5a unchanged.
- **Applied-state knowability**: the pre-`Process` camera-fact snapshot
  (`cam_mul`, `pre_mul`, `rgb_cam`, `cam_xyz`, `linear_max`) fully determines
  the applied gains whenever `cam_mul` is valid — the overwhelmingly dominant
  case, and the only case the as-shot estimator has ever served. In the
  auto-WB-fallback case the actually-applied gains are process-internal and
  unrecorded; the as-shot estimate already degrades to its 5500 K/0 default
  there. No new `BaseImageInfo` fields are added: no R5a or R5b consumer
  needs the applied state beyond what the snapshot provides (standing
  smallest-change rule).

### 1.2 Class scope

| Class | Seam behavior | Status |
|---|---|---|
| 3-color Bayer (AHD) | 3-channel camera RGB | Verified (fixtures, goldens, parity gate) |
| X-Trans (Markesteijn) | 3-channel camera RGB | Verified (RAF fixture) |
| Linear/embedded-demosaic DNG, sRAW | 3-channel; sRAW may arrive WB-pre-applied (`as_shot_wb_applied`, `scale_colors` lines 172-176 pins `pre_mul=1`) | Routed; claims narrowed per TESTING.md precedent |
| Foveon, Leaf, no-CFA | 3-channel processed output | Routed-but-unverified; claims narrowed |
| Genuine 4-color (CMYG/RGBE, `colors==4`) | **4-channel** output under `output_color=0` (`convert_to_rgb` collapses 4→3 only when `output_color` ≠ 0, `postprocessing_utils_dcrdefs.cpp:105-106`; `dcraw_make_mem_image` emits `P1.colors` channels) | §6 ballot item |

Census of `colors==4` cameras in LibRaw 0.22.2 (`src/metadata/identify.cpp`):
Canon PowerShot 600/A5/Pro70/Pro90 IS/G1 (`.CRW` — not routed), Sony DSC-F828
RGBE (`.SRF` — not routed), Nikon E2500-era CYGM Coolpix, and 4-color CFA
declarations via the TIFF/DNG path. The routed-extension overlap is at most
ancient Coolpix `.NEF` files and hypothetical 4-color `.DNG` conversions.

## 2. Characterization (R5a)

Characterization runs in decode, fused into the existing pixel-import seam:
camera `ushort` → 3×3 matrix in `double` → one Q16 encoding, clamped only
at that point. The implementation transforms into a pooled Q16 band capped at
2 MiB for supported camera dimensions and writes each band into Magick's pixel
cache, so there is no managed full-frame copy or second full-frame pixel cache.

**Typed outcomes**, decided per file from the copied facts (matrix validity
decoupled from WB-fact validity):

1. **Usable** — `CamToSrgb` present and non-sentinel. The matrix is
   `M_camera→Rec2020 = M_sRGB→Rec2020 × M_camera→sRGB` (column-vector
   convention; `M_sRGB→Rec2020` is the exact derived matrix from
   `RgbColorSpaceMatrices`). This reproduces the transform LibRaw applies
   today — `out_cam = out_rgb[target] · rgb_cam`
   (`postprocessing_utils_dcrdefs.cpp:99-101`) — with the target factor moved
   in-app.
2. **Derived** — `rgb_cam` is LibRaw's identity sentinel but `cam_xyz` exists:
   the matrix is recovered by the pinned semantic
   (row-normalize `cam_xyz · M_sRGB→XYZ`, invert — `RawWorkingSpaceTests`),
   then composed as in outcome 1.
3. **Uncharacterized passthrough** — no usable matrix from either fact. The
   characterization is identity. This exactly matches today's shipped pixels:
   with no camera transform, LibRaw's own conversion degenerates to identity,
   so these files already contain camera-native samples labeled Rec.2020.
   Documented honestly here rather than regressed to a decode error
   (ruled FINAL, run 158 plan review).

`WhiteBalanceModel.EstimateAsShot` and all camera facts are untouched: facts
remain camera→sRGB, copied at the same pre-configure seam, and pre-R5a
Kelvin/tint values are asserted exactly for every committed RAW fixture.

## 3. Versioning, caching, goldens

`BaseImage.Version` 8 → 9 (decode-visible change); `RenderPipeline.Version`
stays 9 (no render-math change). Caches invalidate through the version bump;
no migration code. The v9 golden set is re-baselined once with a recorded
pre/post attribution report (per-image ΔE summary); ColorChecker budgets are
re-measured on all three RIDs with the standing calibration procedure.

## 4. Validation and budgets (R5a)

Four anchors per TESTING.md: property tests (as-shot neutral maps to neutral;
achromatic preservation through characterization; matrix outcome typing),
source-cited constants with derivation cross-checks, independent
`colour-science` oracle vectors for the composed camera→Rec.2020 matrices
(agent-separated generation), and the ColorChecker ground-truth budget.

Parity gate (checkpoint B, before integration): pre/post-R5a rendered
comparison over the four RAW golden anchors + ColorChecker NEF + Canon 6D,
including Blend and FBDD decode modes on Bayer and X-Trans. Expected sub-1
mean ΔE00 (differences only from double-precision math and single rounding
replacing LibRaw's internal 16-bit matrix path). The measured comparator pilot
freezes the gate at mean ΔE76 ≤ 1.1 and p99 ΔE76 ≤ 9.5; Bayer measured
0.009–0.015 mean and X-Trans 0.79–1.00 mean across Clip, Blend, and FBDD.

Budgets: canonical TESTING.md gates stay binding (complete slider render
≤ 150 ms preview; full export within +5% / +16 MiB). R5a adds: decode-latency
delta ≤ 150 ms full-res / ≤ 45 ms preview, and a ≤ 4 MiB **retained**
private-memory delta vs the direct import, measured deterministically (forced
GC at step boundaries with the result image alive) — async-sampled peaks are
reported for information only, because native-allocator private bytes are not
reproducible run-to-run. The budgets exclude any additional full-frame
allocation; the double-buffered pooled-band pipeline (two ≤ 1.5 MiB buffers,
each band's cache write overlapping the next band's transform) uses no
full-frame import transient. The preview latency budget was recalibrated from
the checkpoint-A 30 ms freeze to 45 ms, and the full-resolution budget from
100 ms to 150 ms, at review (both user-approved 2026-08-19): the measured
preview floor under the 4 MiB constraint is 22–31 ms on the slower review
machine (4–22 ms on a faster one), dominated by the serialized region cache
writes, and the faster alternative — full-frame staging — was declined to
protect the export peak gate; the full-resolution delta measured 86–136 ms
across repeat runs under varying machine load. Measured on the 20 MP Canon
6D: preview delta 22–31 ms / retained 0.0 MiB; full-resolution delta
86–136 ms / retained 2.0 MiB. The opt-in modern-camera compatibility suite reruns at checkpoint C.

The win-x64 ColorChecker observations are recalibrated. Linux-x64 and osx-arm64
remain explicit pending observations until their fresh-process runs complete;
neither is inferred from the Windows result.

## 5. Out of scope for R5a

Everything in §7 (R5b), RCD (R6), highlight-reconstruction research (R7),
profile ToneCurve/LookTable (never consumed — ENDSTATE decision 5), ICC input
profiles, network reads, and the WB re-decode path (standing parked risk).

## 6. Checkpoint-A ballot outcomes (decided 2026-08-19)

1. **Bridge facts-voiding gap — patch deferred to the next native package
   train.** `hplr_get_camera_facts` returns `ABSENT` on any invalid `cam_mul`
   entry before copying `rgb_cam`/`pre_mul`/`cam_xyz`
   (`native/libraw/bridge/src/bridge_facts.cpp:203-205`). Post-R5a, a
   missing-WB file therefore loses the matrix LibRaw still applies today and
   drops to outcome 3 (uncharacterized passthrough). The decoupling patch
   (copy matrix facts independent of multiplier validity) is the committed
   fix and ships with the next maintainer-built native package rebuild, not
   inside R5a — no fixture or compat-corpus file exercises the class. Until
   then this is a **known limitation**: such files render with camera-native
   color unconverted.
2. **4-color inputs — typed rejection.** Under `output_color=0` a `colors==4`
   file emits four channels (§1.2). R5a rejects it as a typed
   unsupported-file outcome with an actionable message, the same surface as
   other unsupported decodes. This is a documented compatibility regression
   for a class with no known real members in the routed formats (at most
   ancient CYGM Coolpix `.NEF` and hypothetical 4-color `.DNG`); a narrow
   LibRaw-color carve-out and a 3×4 in-app path were both declined.

## 7. DCP consumption (R5b — normative, not implemented in R5a)

### 7.1 Scope and sources

`.dcp` camera profiles per the Adobe DNG 1.7.1 specification, split at the
pipeline's natural seam: matrices in decode, HueSatDeltas in render.
`ToneCurve`/`LookTable` are never read (the tone engine is the single
scene→display answer). No profiles ship; reads are local-only: a user-picked
file, the local Adobe `CameraRaw\CameraProfiles` folder, and the DNG's own
embedded profile tags. The picker presents an honest empty state.

### 7.2 Profile inputs

Parsed from the TIFF-IFD profile container: `ProfileName`,
`UniqueCameraModel`, `ColorMatrix1/2`, `ForwardMatrix1/2`,
`CalibrationIlluminant1/2` (defaults per spec when absent; unknown illuminant
values reject the profile), `ProfileHueSatMapDims`, `ProfileHueSatMapData1/2`,
`ProfileHueSatMapEncoding` (linear = 0 default, sRGB = 1),
`ProfileLookTableData` **ignored**, `ProfileEmbedPolicy` honored for embedded
extraction. From the camera/DNG side: `AnalogBalance`, `CameraCalibration1/2`
with the calibration-signature matching rules, `ReductionMatrix1/2` for the
no-ForwardMatrix fallback, `AsShotNeutral`/as-shot `cam_mul`. Unsupported
variants (unexpected dims, missing mandatory tags, non-matching signatures)
reject the profile explicitly — the picker reports it, decode falls back to
the built-in path (§2), never a silent wrong matrix.

### 7.3 Matrix math (as-shot-anchored)

Per the DNG spec's camera-to-XYZ model. The interpolation weight between
illuminant 1 and 2 derives from the **as-shot** white point's correlated color
temperature (inverse-CCT linear weighting, clamped to the pair's range).
Anchoring at as-shot is a deliberate, documented deviation from strict DNG
math (which re-interpolates under the user-selected WB): user WB remains a
render-layer chromatic adaptation, extending the standing WB-accuracy-ceiling
risk rather than making WB decode-affecting.

**The composition must be defined for the balanced seam, not raw camera
coordinates.** The DNG spec's equations (`XYZ_D50 = FM · D · CC⁻¹ · AB⁻¹ ·
camera_raw`, `D` the reference-neutral normalization) act on *unbalanced*
camera values; §1's seam is already as-shot-neutralized by LibRaw (`cam_mul`
applied, per-channel; the remaining `65535/maximum` scale is uniform).
Applying the literal equations at this seam would neutralize twice. The R5b
implementation therefore composes against the balanced seam: with
`seam = diag(cam_mul_normalized) · camera_raw` (up to a uniform exposure
scale, which every downstream stage is invariant to), the decode transform is
`XYZ_D50 = FM · D · CC⁻¹ · AB⁻¹ · diag(cam_mul_normalized)⁻¹ · seam`, where
`D` uses the profile's reference neutral (`AsShotNeutral` for DNGs; the
CameraNeutral implied by `cam_mul` otherwise — the spec derivation must state
the `cam_mul ↔ AsShotNeutral⁻¹` correspondence and its normalization
explicitly, and the R5b plan carries an oracle test that a synthetic profile
with known `CC`/`AB` round-trips a balanced neutral to D50 white exactly).
Files on the missing/auto-WB path (§1.1) have no recorded balancing gains and
therefore fall back to the built-in path — a DCP cannot be applied to a seam
whose neutralization state is unknown. Without a ForwardMatrix, the
ColorMatrix-inverse path with white-point preservation (and `ReductionMatrix`
where applicable) composes against the same balanced-seam definition. Then
Bradford D50→D65 (WHITE_BALANCE.md matrices) and XYZ→Rec.2020
(`RgbColorSpaceMatrices`), composed once in `double` and fused into the same
import seam as §2. The characterized base must satisfy the identical seam
contract (§1) — the profile only replaces the matrix source.

### 7.4 HueSatDeltas (render layer)

Applied scene-linear, before the AgX crossing. The exact sequence per the DNG
spec: working Rec.2020 → linear ProPhoto D50 RGB (Bradford) → HSV. When
`ProfileHueSatMapEncoding=1` (sRGB), **only the V coordinate is sRGB-encoded**
before table lookup, and the modified V is inverse-decoded back to linear
after the deltas apply — H and S are never encoded; the encoding tag is
inapplicable (ignored) when `ValueDivisions` is 1, where the map is 2.5D and
lookup interpolates over hue and saturation only. Table interpolation is
trilinear (bilinear in the 2.5D case) over (hue-shift°, saturation-scale,
value-scale) with hue wraparound; saturation and value scales clamp results
to valid HSV; the spec's dual-table case interpolates between the illuminant
pair's tables sharing §7.3's as-shot weight, and a single-table profile uses
that table for all weights. Then HSV → linear ProPhoto → working space. Runs
only when the selected profile carries tables.

### 7.5 Atomicity, settings, cache identity

Profile selection is decode-affecting: it joins the `BaseDecodeSettings`
projection and `CacheKey` (identity token including a content hash), so a
profile change triggers the standard newest-wins replacement decode. The
resolved profile payload — matrices and interpolated HueSat tables — is bound
to `BaseImageInfo`, so render consumes the tables from the same base whose
pixels the matching matrix produced; matrix and tables switch atomically when
the replacement base installs. Persistence, preset/copy/MCP transfer policy,
and the v2 settings-schema addition are decided in the R5b plan (camera-
specific selection is expected to be excluded from transfer surfaces).

### 7.6 Discovery

All profile-content reads route through the live-availability policy
(no background hydration); embedded-profile extraction happens inside the
already-gated decode. Adobe-folder matching keys on `UniqueCameraModel`
against normalized camera identity; mismatch degrades to embedded/built-in,
never an error state. A user-picked file on cloud-placeholder storage follows
the standing explicit-hydration rules.

### 7.7 R5b validation and budgets

Synthetic `.dcp` fixtures with known constants (generated agent-separately —
Adobe profiles are copyrighted and never committed); parser, interpolation,
and HSV round-trip oracle vectors via the `colour-science` script; integration
tests for all three discovery sources, precedence, corrupt/missing profiles,
persistence, cache invalidation, and the empty state. Full-resolution
active-profile decode/export gates on the Canon 6D plus the preview slider
gate, with numeric peak-memory deltas — frozen in the R5b plan. Adobe-profile
behavior is additionally verified manually at the R5b look sign-off.
