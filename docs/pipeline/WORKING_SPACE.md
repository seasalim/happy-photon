# Working Space: linear Rec.2020, D65

The canonical `BaseImage` color space and the transforms into and out of it. This is the
NEWRAW stage 1 / NEWJPEG stage 1 specification and its provenance record: every constant
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

Rationale is ENDSTATE.md decision 1 — real primaries give the densest Q16 code usage, the
AgX formulations are defined against them, and BT.2100 shares them, so future HDR output
inherits the basis. Numeric representation stays Q16 storage with `double` computation
(ENDSTATE.md decision 9).

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

The render normalization fold (RENDER.md §4) for the bare working→display matrix is its
largest row positive sum, **1.6604910021084345**. Neutral (1,1,1) therefore lands at
0.6022 before the tone LUT refunds the fold — the existing mechanism, now universal.

## 3. Decode: raw

`RawBaseLoader` selects the wide output space through the bridge's existing output
configuration (`output_color`); LibRaw applies its camera matrix into BT.2020 instead of
sRGB. This is configuration data — no bridge, native, or ABI change. The shared
linear/sRGB output configuration keeps its current value and its existing callers.

**The camera-matrix fact stays camera→sRGB.** Facts are copied after unpack and before the
output configuration is applied, so `camera_to_srgb` is what LibRaw computed under its own
default output space. That is a semantic worth proving, not assuming, and invariance
across output selections would only prove configuration-independence. The oracle:

> For a three-channel camera, let `A = camera_from_xyz · (sRGB→XYZ)`, row-normalized so
> each row sums to 1. Then `inverse(A)` reproduces `camera_to_srgb`. Substituting
> Rec.2020→XYZ for sRGB→XYZ does not.

The row-sum-1 convention and the `pre_mul / cam_mul` projection it implies are already
documented in WHITE_BALANCE.md §5.2 and are unchanged. A camera→wide matrix would be a new
fact behind a bridge ABI bump; this run does not need one.

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

The working→display conversion composes with the white-balance matrix into the render's
single chromatic matrix. It is evaluated with the one tone LUT in a fused storage pass,
with the same intermediate Q16 quantization, keeping RENDER.md's matrix + one-LUT
contract and its normalize/fold refund. `asShot` no longer skips the stage; its factor
remains exact identity, so the composed matrix is exactly the §2 working→display matrix.

This placement is temporary by design. In the ENDSTATE graph the display convert is node
7, after the AgX outset; R4 moves it there when the crossing lands. Until then it sits
where today's pipeline already crosses into display primaries, which is before the tone
LUT's sRGB encode.

Two consequences are deliberate:

- Colors outside the display gamut clamp at the matrix (negative coefficients drive them
  to 0), exactly as they clamped at decode before this change. Wide-gamut *output* is
  R2's job; R1 preserves the gamut through editing, not through export.
- The raw near-clip statistic thresholds base channels before the matrix, so on saturated
  color, wide primaries would under-report sensor clip. It converts to the display basis
  inside its scan and keeps its current published behavior. ENDSTATE decision 7's
  scene-referred redefinition remains R4's.

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
