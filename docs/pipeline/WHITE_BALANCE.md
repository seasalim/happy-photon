# White Balance Model

Happy Photon models white balance as chromatic adaptation in linear sRGB rather than
as a display-space red/blue adjustment. `WhiteBalanceModel` and
`ChromaticAdaptation` produce the 3×3 matrix consumed by RENDER.md §4, while reference
values and round trips are pinned by the tests summarized in §9.

## 1. Semantics (Lightroom convention)

The Kelvin slider states *what the scene illuminant was*. The base is already balanced
for the as-shot illuminant (decode applied camera WB; as-shot neutral = (1,1,1)).
Rendering neutralizes the *claimed* illuminant instead:

- `kelvin == asShotKelvin && tint == asShotTint` → identity (and mode `asShot` skips
  the matrix entirely, so identity is exact regardless of estimation quality).
- Raising Kelvin above as-shot → image gets **warmer**; lowering → cooler.

UI: Kelvin slider 2000–12000, logarithmic; Tint slider −100 (green) … +100 (magenta).

Modes (`EditSettings.wb.mode`):

| Mode | Meaning |
|------|--------|
| `asShot` | identity; default |
| `custom` | user Kelvin/tint |
| `preset` | named target from §6 table |
| `picked` | eyedropper gains (§7), matrix = diag(gains) |

## 2. White point from (Kelvin, tint)

Two loci, blended, so that the daylight range hits real daylight illuminants (D65 at
6504 K) while tungsten temperatures follow the Planckian locus (Illuminant A at 2856 K).

**Planckian branch (T ≤ 4000 K)** — Krystek (1985) approximation in CIE 1960 UCS.
Note the sign pattern of the v denominator (− linear, + quadratic); getting it wrong
produces negative v (§9 pins Illuminant A to catch this):

```
u_P(T) = (0.860117757 + 1.54118254e-4·T + 1.28641212e-7·T²)
       / (1 + 8.42420235e-4·T + 7.08145163e-7·T²)
v_P(T) = (0.317398726 + 4.22806245e-5·T + 4.20481691e-8·T²)
       / (1 − 2.89741816e-5·T + 1.61456053e-7·T²)
```

Convert to xy: `x = 3u/(2u − 8v + 4)`, `y = 2v/(2u − 8v + 4)`.

**Daylight branch (T ≥ 4500 K)** — CIE daylight locus:

```
4500 ≤ T ≤ 7000:  x_D = −4.6070e9/T³ + 2.9678e6/T² + 0.09911e3/T + 0.244063
7000 <  T ≤ 12000: x_D = −2.0064e9/T³ + 1.9018e6/T² + 0.24748e3/T + 0.237040
y_D = −3.000·x_D² + 2.870·x_D − 0.275
```

**Blend zone (4000 < T < 4500):** with `q = (T − 4000)/500`, use the smoothstep weight
`w = q²(3 − 2q)` and `xy = lerp(xy_P(T), xy_D(T), w)`. Both formulas are valid there;
the loci differ by Δy ≈ 0.007 at 4000 K. Smoothstep (not linear `q`) matters: its zero
derivative at both ends joins the branch *derivatives* as well as their values, so the
locus is C1 — a linear blend would only remove the positional jump.

**Tint** is applied in uv: convert the locus xy → uv
(`u = 4x/(−2x + 12y + 3)`, `v = 6y/(−2x + 12y + 3)`), then

```
v' = v + tint · 0.00025        // +tint claims green; neutralization renders magenta
```

and convert `(u, v')` back to xy, then to XYZ with Y = 1:
`X = x/y, Y = 1, Z = (1 − x − y)/y`.

## 3. Chromatic adaptation (Bradford)

```
M_A = [ 0.8951  0.2664 −0.1614      M_A⁻¹ = [ 0.9869929 −0.1470543  0.1599627
       −0.7502  1.7135  0.0367               0.4323053  0.5183603  0.0492912
        0.0389 −0.0685  1.0296 ]            −0.0085287  0.0400428  0.9684867 ]

cone_src = M_A · XYZ(kelvin_slider, tint_slider)     // claimed scene illuminant
cone_dst = M_A · XYZ(asShotKelvin, asShotTint)       // what the base is balanced to
M_CAT    = M_A⁻¹ · diag(cone_dst / cone_src) · M_A
```

## 4. Full pixel matrix (linear sRGB in, linear sRGB out)

```
M = M_XYZ→sRGB · M_CAT · M_sRGB→XYZ

M_sRGB→XYZ = [ 0.4124564 0.3575761 0.1804375     M_XYZ→sRGB = [ 3.2404542 −1.5371385 −0.4985314
               0.2126729 0.7151522 0.0721750                   −0.9692660  1.8760108  0.0415560
               0.0193339 0.1191920 0.9503041 ]                  0.0556434 −0.2040259  1.0572252 ]
```

The render stage receives `M` and performs the normalization/fold described in
RENDER.md §4. For `picked`, `M = diag(g_r, g_g, g_b)` directly (no CAT).

## 5. Inversion & as-shot estimation

### 5.1 The single inverse: uv → (Kelvin, tint)

```csharp
public static (double kelvin, double tint) EstimateKelvinTintFromUv(double u, double v);
```

1. Binary search T ∈ [2000, 12000] for `u_locus(T) = u`. Temperature is determined by
   **u alone** — the tint offset moves only v and never u. `u_locus` is strictly
   decreasing in T over the blended locus (asserted by §9 test 6), so the search is
   well-posed; clamp to the bounds on overflow.
2. `tint = (v − v_locus(T)) / 0.00025`, clamped to the slider range (±100).

A nearest-point fit in 2D uv would absorb tint into temperature, so the implementation
uses the one-dimensional u solve above. Direct uv/model inputs are finite-checked rather
than allowing NaN into a render matrix.

### 5.2 As-shot anchor estimation

```csharp
public static (double kelvin, double tint) EstimateAsShot();
```

- Non-raw bases: **(6504, 0)** — D65 by construction of the normalize step (the loader
  hardcodes this; it never calls the estimator).
- Raw bases: **(5500, 0)**. This is a documented fallback, not a measurement. The Kelvin
  display is relative, while `asShot` mode remains exact identity regardless (§1).

LibRaw's `rgb_cam` consumes daylight-balanced camera values: each row is normalized to
sum to 1, with the discarded row scale stored in `pre_mul`. A capture neutral must
therefore be projected as `pre_mul / cam_mul`. Projecting `1 / cam_mul` omits that
reference and fabricates an illuminant even when the result happens to look plausible.
Bridge ABI v1 exposes neither `pre_mul` nor `cam_xyz`, and the normalization makes the
missing scale unrecoverable from `rgb_cam`.

`RawBaseLoader` still preserves the RGB or native four-channel `CamMul`/`CamToSrgb`
facts in `BaseImageInfo`, but `EstimateAsShot` returns the fallback until bridge ABI v2
exposes `pre_mul` and/or `cam_xyz`. That ABI-v2 work item must restore a measured anchor
using `pre_mul / cam_mul`; it must not revive the former `1 / cam_mul` projection.

### 5.3 Display approximation for picked gains

The UI shows grayed Kelvin/tint for gain-based modes. Formula: the white the gains
neutralize is `w_srgb = normalize(1/g_r, 1/g_g, 1/g_b)` in linear sRGB;
`XYZ = M_sRGB→XYZ · w_srgb` → uv → `EstimateKelvinTintFromUv`. This is a D65-anchored
approximation (it ignores the as-shot anchor), adequate for the grayed display —
it is never used in rendering.

## 6. Presets (fixed targets)

| Preset | Kelvin | Tint |
|--------|--------|------|
| Daylight | 5500 | +10 |
| Cloudy | 6500 | +10 |
| Shade | 7500 | +10 |
| Tungsten | 2850 | 0 |
| Fluorescent | 3800 | +21 |
| Flash | 5500 | 0 |
| Auto | computed (§8) | computed |

Selecting a preset writes `mode: "preset", preset: <name>` plus the resolved
kelvin/tint (so renders never depend on a lookup-table version).

## 7. Eyedropper ("picked")

The eyedropper samples a 5×5 region of the **base** (linear, pre-matrix) at the clicked
preview position. Picks whose mean has any channel > 0.95 or < 0.005 are rejected as
clipped or below the noise floor, and the UI asks for a neutral mid-tone. Gains are
`g = (mean_G/mean_R, 1, mean_G/mean_B)`,
clamped to [0.2, 5]. Store `mode: "picked", gains: g`. For UI feedback, display the
grayed approximate kelvin/tint from §5.3.

## 8. Auto (gray-world)

Auto is an on-demand UI action rather than a render mode. It downsamples the base to
≤ 64px, drops pixels with any channel > 0.98 or < 0.005, computes the mean, and stores
the §7 gains as `picked`. The result is deterministic for a given base.

## 9. Verification (`WhiteBalanceModelTests`)

1. Identity: matrix for (anchor == target) is I within 1e-6; `asShot` mode bypasses.
2. Warm direction: target 6500 vs anchor 3000 → R gain > B gain.
3. Tint sign: (5500, +50) suppresses G relative to (5500, 0).
4. **D65 pin (daylight branch):** XYZ(6504, 0) ≈ (0.9504, 1, 1.0888), each component
   within 2e-3.
5. **Illuminant A pin (Planckian branch):** (u, v)(2856) ≈ (0.2559, 0.3496), each
   within 1e-3. (This is the test that catches the v-denominator sign error.)
6. Locus continuity + monotonicity: over T = 2000…12000 in 25 K steps (tint 0),
   adjacent white points differ by < 2.1e-3 in uv (the Planckian end is steepest:
   the 2000→2025 K step measures ≈ 2.03e-3), no jump across the 4000–4500 blend zone,
   and `u_locus` is strictly decreasing over the whole grid (validates §5.1's search).
7. Round-trip: `EstimateKelvinTintFromUv` ∘ (kelvin, tint → white) recovers K within
   50 K and tint within 2 units across a grid (2500–10000 K, tint −50…+50) — the
   u-solve inverse is near-exact, so these bounds are comfortable, not tight.
8. Normalization contract (with RENDER §4): for any grid matrix,
   `Mn·(1,1,1)ᵀ ≤ 1 + 1e-9` per component.
9. RAW fallback: committed camera facts pin the row-sum-1 `rgb_cam` convention and
   non-uniform real `cam_mul`; every committed RAW fixture reports exactly (5500, 0)
   until the ABI-v2 reference facts are exposed.
