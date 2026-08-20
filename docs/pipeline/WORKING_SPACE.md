# Working Space: linear Rec.2020, D65

The canonical `BaseImage` color space and the transforms into and out of it. This is the working-space specification and its provenance record: every constant
below is derived here from published primaries or cited to a public standard. No GPL
implementation (darktable, RawTherapee, Blender) was consulted.

The numeric vectors in §7 are the run's oracle. They are derived from the citations in
§8, independently of the implementation, and the implementation consumes them rather
than regenerating them.

## 1. Definition

`BaseImage` pixels are **linear light, ITU-R BT.2020 primaries, D65 white, Q16 unsigned**,
orientation applied, no look baked in. Nothing else about the contract changes:
scene-referred values, 1.0 = sensor/display white, and `BaseImageInfo` facts keep their
current meanings.

Rationale: real primaries give the densest Q16 code usage, the
AgX formulations are defined against them, and BT.2100 shares them, so future HDR output
inherits the basis. Numeric representation stays Q16 storage with `double` computation.

| Quantity | Value |
|----------|-------|
| Red primary | x 0.708, y 0.292 |
| Green primary | x 0.170, y 0.797 |
| Blue primary | x 0.131, y 0.046 |
| White | D65, x 0.3127, y 0.3290 |
| Transfer | linear (no encoding) |

## 2. Derived matrices

Derived from §1 by the standard construction (primary matrix scaled so RGB (1,1,1) maps
to the white point), then verified against the published values cited in §8 and the
committed `colour-science` oracle. Row-major, `[out][in]`.

```
Rec.2020 → XYZ (D65)              XYZ → Rec.2020 is its exact inverse
  0.6369580483012914  0.1446169035862083  0.1688809751641721
  0.2627002120112671  0.6779980715188708  0.0593017164698620
  0.0000000000000000  0.0280726930490874  1.0609850577107910

Rec.2020 → sRGB (both linear, D65) = (XYZ→sRGB) · (Rec.2020→XYZ)
 +1.6604910021 -0.5876411388 -0.0728498633
 -0.1245504745 +1.1328998971 -0.0083494226
 -0.0181507634 -0.1005788980 +1.1187296614

sRGB → Rec.2020 (both linear, D65)
 +0.6274038959 +0.3292830384 +0.0433130657
 +0.0690972894 +0.9195403951 +0.0113623156
 +0.0163914389 +0.0880133079 +0.8955952532
```

The Rec.2020→sRGB matrix now runs only in finalization, after every shared edit. It is
not normalized or folded into the tone stage; it converts display-linear Rec.2020 to
the requested target immediately before the common sRGB transfer is encoded.

## 3. Decode: raw

`RawBaseLoader` selects camera-native output through the bridge's existing
configuration (`output_color = 0`). LibRaw performs black subtraction, camera-WB
scaling, normalization, demosaic, and configured highlight handling, but applies no
output-space matrix. `CameraRgbCharacterization` composes the copied camera→sRGB fact
with the exact §2 sRGB→Rec.2020 matrix and fuses it into the one Q16 decode write. This
is a managed configuration/loader change — no bridge, native package, or ABI change.
See CHARACTERIZATION.md for the neutralization state, typed fact outcomes, and R5b DCP
replacement contract.

**The camera-matrix fact stays camera→sRGB.** Facts are copied after unpack and before the
output configuration is applied, so `camera_to_srgb` is what LibRaw computed under its own
default output space. That is a semantic worth proving, not assuming, and invariance
across output selections would only prove configuration-independence. The oracle:

> For a three-channel camera, let `A = camera_from_xyz · (sRGB→XYZ)`, row-normalized so
> each row sums to 1. Then `inverse(A)` reproduces `camera_to_srgb`. Substituting
> Rec.2020→XYZ for sRGB→XYZ does not.

The row-sum-1 convention and the `pre_mul / cam_mul` projection it implies are already
documented in WHITE_BALANCE.md §5.2 and are unchanged. The camera→working matrix is a
decode-local composition, not a persisted or bridge-owned fact.

## 4. Decode: non-raw

Profiled Standard and HEIC sources normalize into the working space by ICC transform to
a **linear-Rec.2020 target profile constructed from §1**. Unprofiled non-CMYK sources and
the sRGB thumbnail proxy take the equivalent faster path: apply the IEC sRGB EOTF, then
the exact §2 sRGB→Rec.2020 matrix. The ICC target is a matrix/TRC display profile per
ICC.1:2010, carrying `desc`, `cprt`, `wtpt`, `chad`, the three `XYZType` colorants, and
three `curv` TRCs with gamma 1.0. Colorants are Bradford-adapted to the ICC PCS D50 white
(0.9642, 1.0000, 0.8249); the adaptation goes in `chad`:

```
chad (Bradford, D65 → ICC D50)        D50-adapted colorants (rXYZ gXYZ bXYZ as columns)
 +1.04788603 +0.02291869 -0.05021606   +0.67348019 +0.16567116 +0.12504864
 +0.02958179 +0.99048358 -0.01707873   +0.27904260 +0.67534454 +0.04561290
 -0.00925190 +0.01507256 +0.75167814   -0.00193351 +0.02998282 +0.79685064
```

The small negative red Z is real and representable — ICC `XYZType` is signed
s15Fixed16 — and is what BT.2020's red corner becomes under D50 adaptation.

Transforming to a gamma-1.0 target leaves the pixels already linear, so the loader's
existing linearization step must not run a second transfer over them.

Why a real wide target rather than transform-to-sRGB-then-matrix: an iPhone Display-P3
source has content outside sRGB, and the sRGB hop would clip it before it ever reached the
wide base — which is the gamut this run exists to preserve. §7 pins that with vectors.

## 5. Render placement

The AgX rework completed this placement: RAW composes the AgX inset with white balance,
evaluates the tone engine, and applies the AgX outset; standard sources use white
balance and the retained display-referred chain. Chroma and all detail stages then run
on encoded display Rec.2020. Only after resize and optional output sharpening does
finalization decode, convert display-linear Rec.2020 to sRGB or Display P3, clamp, and
encode. Preview always selects sRGB.

The crossing's normalize/fold machinery applies only to `M_inset · M_WB` (or `M_WB`
for crossing off), with the fold refunded by the active tone regime. Target matrices
never affect shared edits. Scene-referred RAW highlight statistics sample after WB and
exposure but before the inset; `RawNearClip` keeps its legacy decoded display-basis
meaning (RENDER.md §7).

## 6. White balance

The basis splits, deliberately:

- The render matrix `M = (XYZ→Rec.2020) · M_CAT · (Rec.2020→XYZ)`, and picked/auto gains
  are working-space diagonals, so the grayed kelvin display for gains projects through
  Rec.2020→XYZ.
- RAW as-shot estimation stays in **sRGB**, because the camera-matrix fact it projects
  through is still camera→sRGB (§3). Its published values do not move.

`asShot` remains exact identity in the white-balance factor, and the WHITE_BALANCE.md §2
locus, tint, and inversion math are untouched.

## 7. Oracle vectors

Encoded codes are 8-bit; linearization is the IEC 61966-2-1 sRGB EOTF, which Display P3
shares. Working-space values are linear Rec.2020; Q16 codes are `round(v · 65535)` after
clamping to [0,1].

**Display-P3 source → working space** (the gamut-preservation case). The sRGB column is
what the old path produced — clipped at both ends.

| Patch | P3 code | linear Rec.2020 | Q16 | linear sRGB (old path) |
|-------|---------|-----------------|-----|------------------------|
| red | 255, 0, 0 | +0.75383303, +0.04574385, −0.00121034 | 49402, 2998, 0 | **+1.22494018**, **−0.04205695**, −0.01963755 |
| green | 0, 255, 0 | +0.19859737, +0.94177722, +0.01760172 | 13015, 61719, 1154 | **−0.22494018**, +1.04205695, −0.07863605 |
| orange | 255, 128, 0 | +0.79670236, +0.24903635, +0.00258918 | 52212, 16321, 170 | **+1.17638448**, +0.18288198, −0.03661197 |
| neutral | 128, 128, 128 | +0.21586050 ×3 | 14146 ×3 | +0.21586050 ×3 |

P3 red falls a hair outside Rec.2020 on blue (−0.0012) and clamps to 0 in unsigned Q16.
That is expected and negligible; it is not a defect to work around.

**sRGB source → working space** (validates the constructed target through a source profile
this project does not build).

| Patch | sRGB code | linear Rec.2020 | Q16 |
|-------|-----------|-----------------|-----|
| red | 255, 0, 0 | 0.62740390, 0.06909729, 0.01639144 | 41117, 4528, 1074 |
| green | 0, 255, 0 | 0.32928304, 0.91954040, 0.08801331 | 21580, 60262, 5768 |
| blue | 0, 0, 255 | 0.04331307, 0.01136232, 0.89559525 | 2839, 745, 58693 |
| neutral | 128, 128, 128 | 0.21586050 ×3 | 14146 ×3 |

Fixture pixel codes and their expectations both come from these tables, never from a round
trip through the profile builder — otherwise a builder defect could cancel between the
source and the target and leave the test green.

## 8. Sources

- **ITU-R BT.2020-2** (2015), *Parameter values for ultra-high definition television
  systems* — the primaries and white point in §1.
- **IEC 61966-2-1:1999** — sRGB primaries and transfer function.
- **SMPTE EG 432-1** / Apple's published *Display P3* description — P3 primaries
  (0.680/0.320, 0.265/0.690, 0.150/0.060), D65 white, sRGB transfer.
- **ISO 15076-1 / ICC.1:2010** — profile structure, required tags, `XYZType`, `curv`,
  `chad`, and the D50 PCS illuminant.
- **W3C CSS Color 4 conversion appendix** — the published matrix values §2 is checked
  against, sourced there from the standards above.
- **LibRaw 0.22 documentation** for `output_color`; the row-sum-1 camera-matrix convention
  as already recorded in WHITE_BALANCE.md §5.2.
- `Tests/assets/color-science-oracle.json` — the committed BSD `colour-science` oracle
  (TESTING.md §4.1 anchor 3), which independently carries the linear sRGB and linear
  Rec.2020 matrices used above.

## 9. Output targets

Finalization converts display-linear Rec.2020 → **the selected output space** after
all shared editing. sRGB is the default and Display P3 is opt-in; preview always uses
sRGB. P3 shares the IEC 61966-2-1 transfer, so only the final matrix and embedded
profile differ.

**Display P3** — SMPTE EG 432-1 primaries with the D65 white point and the sRGB transfer,
as published by Apple: R (0.680, 0.320), G (0.265, 0.690), B (0.150, 0.060).

```
Rec.2020 → Display P3 (both linear, D65)      Display P3 → XYZ (D65)
 +1.3435782526 -0.2821796705 -0.0613985821     +0.4865709486 +0.2656676932 +0.1982172852
 -0.0652974528 +1.0757879158 -0.0104904631     +0.2289745641 +0.6917385218 +0.0792869141
 +0.0028217873 -0.0195984945 +1.0167767073     +0.0000000000 +0.0451133819 +1.0439443689
```

These target matrices are evaluated directly in the trailing decode → convert → encode
pass. They do not participate in render normalization or consume tone-table range.

### 9.1 Embedded profile

P3 exports embed `DisplayP3-v4.icc` from Compact ICC Profiles (CC0, 480 bytes), already
committed for the gamut fixture and verified there against the D50-adapted P3 colorants to
7e-6. Exported files are read by other people's software, so a widely-deployed profile beats
one we generate; the constructed working-space profile (§4) stays internal and is never
embedded. The embedded profile's primaries **and** its parametric transfer must be checked
against the pixels the renderer produced — a file tagged with primaries it was not rendered
for is this feature's characteristic failure and is invisible on inspection.

### 9.2 Oracle vectors

Equivalence — the same color encoded in each target, both under the sRGB transfer. A P3
export of in-gamut content should land on these codes:

| Color | sRGB code | Display P3 code |
|-------|-----------|-----------------|
| red | 255, 0, 0 | 233.959, 51.073, 35.333 |
| green | 0, 255, 0 | 116.892, 251.242, 76.065 |
| blue | 0, 0, 255 | 0, 0, 244.695 |
| mid grey | 128, 128, 128 | 128, 128, 128 |

Round trip — the synthetic native-P3 fixture through the Q16 Rec.2020 base and back out to
a P3 export:

| Patch | P3 source | base Q16 | recovered P3 code |
|-------|-----------|----------|-------------------|
| red | 255, 0, 0 | 49402, 2998, 0 | 254.99, 0.00, **4.05** |
| green | 0, 255, 0 | 13015, 61719, 1154 | 0.00, 255.00, 0.02 |
| orange | 255, 128, 0 | 52212, 16321, 170 | 255.00, 128.00, 0.02 |
| neutral | 128, 128, 128 | 14146, 14146, 14146 | 128.00, 128.00, 128.00 |

Red's blue channel recovers as 4, not 0. P3's red corner sits a hair outside Rec.2020
(−0.0012 in blue, §7), unsigned Q16 clamps that to zero, and the return trip converts the
clamped value back as a small positive. It is a storage-representation artifact, stated here
so it is recognized rather than re-derived or absorbed into a tolerance later.

### 9.3 Resolved limits

- Every nonlinear edit is target-independent. At the renderer's Q16 pre-encode
  boundary, edited in-gamut sRGB/P3 agreement is gated at mean ΔE00 ≤ 0.034 for the
  synthetic worst case and ≤ 0.053 for the real-RAW full-combo case, both with output
  sharpening off and on. The encoded 8-bit observation remains informational because
  target-code quantization alone contributes about 0.2 mean ΔE00.
- Capture sharpen, chroma NR, and output sharpen all reference `Rec2020Luminance`, the
  exact Rec.2020→XYZ Y row from §2:
  `(0.2627002120112671, 0.6779980715188708, 0.0593017164698620)`. They run before the
  target convert; the former rounded BT.709 basis is retired.
