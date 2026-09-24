# Render: `BaseImage` × `EditSettings` to Pixels

`RenderPipeline` is the single pixel path shared by preview, histogram, and export.
The resting policy is an optional `RenderExecutionOptions` on the single display-stage sequence.
The formulas below explain how it preserves linear-light headroom while reducing the
tonal work to one quantization step. All Magick.NET processing remains Q16.

## 1. Stage order (fixed)

```
1 Geometry     rotate90 → fused horizon/keystone/aspect/radial warp → crop
2 DCP HueSat   optional scene-linear ProPhoto HSV profile map (§2.1)
3 Matrix/locals WB → locals → RAW AgX inset (§2.2–2.4, §4)
4 Tone LUT     source-kind tone regime, fused with matrix storage (§5)
5 Matrix       crossing on: AgX outset; crossing off: identity
6 Chroma       one fused OKLCh color-mixer/saturation/vibrance pass (§6)
7 Detail       luminance/chroma NR → capture sharpen (§9)
8 Output       linear resize → output sharpen → effects → target convert → encode
               (OUTPUT.md)
```

`RenderGeometry` owns one clone. Quarter-turns are lossless; active horizon/manual
terms share one inverse bilinear warp, skipped at identity. The corrected frame keeps
source aspect and shrinks to covered bounds without upsampling. Crop is normalized
on that frame, so even full-image crop remains blank-free.

Centered keystone coordinates use `w = 1 + a·y + b·x`, with Vertical and Horizontal
slider values mapping to `a,b = −value/200`. Aspect applies `sx=e^s`, `sy=e^−s`,
`s=value/400`. Manual radial is destination-to-source
`f(ru)=ru·(1+k·ru²)`, `k=−value/400`, where `ru` uses the source half-diagonal.
Beyond `ru=1`, `f` continues linearly with value `1+k` and slope `1+3k`; the forward
projection uses the closed-form inverse of the cubic below that knee and division
above it. This keeps the map monotone at every slider setting.

### 1.1 Request contract

`RenderRequest`, `RenderOptions`, and `RenderResult` are defined in
[RenderContracts.cs](../../Services/RenderContracts.cs). The request includes output
color space and sharpening, source saturation, and an internal locals-frame override.
Options select statistics, scopes, masks, and preview-pixel preparation.

Preview forces sRGB and output sharpening Off. Export proof uses a separate display
render plus proof finalizer; shared edits remain target-independent.
`RenderGeometry.Apply` supplies the one owned clone, leaving the caller's base immutable.
Every downscale runs in linear light with the same filter. Preview resizes the neutral
base before tone; export resizes after tone. These do not commute, so TESTING.md §3
bounds their deliberate performance-driven approximation.

## 2. Why matrix → single LUT → matrix

WB and AgX inset/outset are 3×3 matrices enclosing a per-channel 1D function.
`AgxCrossing` evaluates inset → exact 65,536-entry interpolated tone table → outset in
`double`, then writes Q16 once. Crossing-off uses WB → retained display chain →
identity. One fused pass avoids clipped intermediates and requantization, bounding
slider ticks plus optional chroma/detail work.

### 2.1 DCP HueSat stage

An installed DCP HueSat payload runs after geometry, before WB/locals and AgX. Convert
Rec.2020 D65 → linear ProPhoto D50 → HSV → map → ProPhoto → working space. For sRGB
table encoding, encode only V before lookup and inverse-decode afterward; H/S remain
linear. ValueDivisions=1 is 2.5D and ignores that encoding tag. Dual tables share
decode's as-shot weight; single tables do not vary with it. The 65³ Q16 lattice cache
keys profile content and as-shot weight. Trilinear evaluation fuses into the crossing's
working array with one read/write for interactive/resting paths; no active table
preserves exact output.

### 2.2 Local color before tone

Locals compose in creation order after global WB in linear Rec.2020 and before
AgX inset/tone. Render setup resolves the effective global white from Custom/Preset
Kelvin+tint, the base's as-shot estimate, or `EstimateFromGains` for Picked.
Each color-active local prepares `M = 2^EV · S · A` once: A is
`CreateMatrix(1e6/(1e6/Kg - temperature), tg + tint, Kg, tg)` with the WB model's
white-point limits; S scales chroma about Rec.2020 luminance by `1+saturation/100`.
Pixels blend linear results as `v += weight * (M*v - v)`, never parameters.
RAW starts with WB divided by the existing composed inset×WB Fold, applies locals,
then the unnormalized inset. Standard starts with its normalized WB. Both retain
the existing Fold refund. No intermediate image or mask field is allocated and
no local result is clamped; only tone input handles negatives (RAW's existing
non-positive branch, standard's `Max(0, value)`), preserving overflow above one.
Unchanged pixels keep the exact LUT path. Monochrome bases ignore local color
terms while local Exposure preserves equal RGB channels.

### 2.3 Local Luminance Range

The optional `luminance` range is serialized as described in §8.
An enabled, non-open window multiplies the geometric weight. The fused locals
loop computes exact double OKLab L once, only when a restricted local has nonzero
geometry. Classification uses the original post-DCP/global-WB Rec.2020 pixel with
Fold undone, before locals, global exposure, inset, and tone. Restricted exposure
also prepares the RAW WB basis. Open endpoints include extended L; shoulders fall
outward by smoothstep. Equal interior endpoints with zero softness select nothing.
Off and fully open ranges retain the previous scalar/color paths exactly.

### 2.4 Local Hue Range and picking

The optional `hue` range is serialized beside `luminance` (§8).
An enabled hue term multiplies geometry and luminance by a circular full-weight
window of half-width width/2, outward smoothstep shoulders, and chroma reliability
`smoothstep((C - .01) / .03)`. Full-circle hue still applies reliability; width and
softness both zero select nothing. Monochrome ignores hue without disabling either
geometry or luminance. The fused evaluator shares the original pixel's three cube
roots across all ranges and computes C/hue only after nonzero geometry and
luminance. No classification image is retained. No-hue paths remain bit-exact.

## 3. Notation

- `E(x)`: sRGB encode. `E(x) = 12.92x` for `x ≤ 0.0031308`, else `1.055·x^(1/2.4) − 0.055`.
- `D(y)`: inverse (decode). `y/12.92` for `y ≤ 0.04045`, else `((y+0.055)/1.055)^2.4`.
- `clamp01(x) = min(max(x, 0), 1)`. Slider ranges are the existing UI ranges.

## 4. Matrix stages

`WhiteBalanceModel` yields a 3×3 matrix `M_WB` in linear Rec.2020
(WHITE_BALANCE.md §4). Crossing-on composes `M = M_inset · M_WB`; crossing-off uses
`M = M_WB`. Before use:

```
normScale = max over rows i of Σ_j max(M[i,j], 0)     // ≥ 1 ⇒ some input could exceed 1
Mn        = M / normScale                              // outputs of [0,1]³ stay ≤ 1
fold      = normScale                                  // refunded inside the tone LUT
```

The fused evaluator interpolates the LUT on the unrounded `double` matrix result;
intermediate index or Q16 rounding is forbidden. Crossing-on refunds the fold exactly
once as `+log2(fold)` inside its log encoding, then applies the AgX outset after the
tone table. Crossing-off refunds it in the exposure multiplier. `WbMode.AsShot` keeps
`M_WB = I`; the normalized RAW input is therefore exactly the inset, whose fold is 1.
The working→sRGB or Display P3 matrix is not part of either stage—it runs after all
shared edits in finalization (WORKING_SPACE.md §9).

## 5. Tone regimes

RAW uses [TONE_ENGINE.md](TONE_ENGINE.md)'s exposure → log2 → sigmoid → display
curve chain. Contrast changes slope, Highlights shoulder, Shadows toe; post-gain 0.18
stays anchored for all Contrast/source bias. Brightness/base look are ignored.
Standard sources use the display-referred chain below; there is no automatic exposure
trigger or persisted regime toggle.

`ToneLut.Compose(ToneParams p) → ToneLuts` is pure and unit-testable. Its three
per-channel `double[65536]` arrays share the master array for channels without curves.
Entry `i` uses linear post-matrix `v = i/65535`. Display-domain operators require [0,1]:
the marked clamps keep §5.2/§5.3 monotone (Contrast +100 can reach ≈ 3.1 before
clamping).

```
g  = 2^(EVuser + EVsource) · fold        // source bias plus relative user exposure
a  = v · g                               // exposure (may exceed 1 — that's the point)
b  = shoulder(a, k)                      // §5.1  highlight recovery (negative Highlights)
c  = min(b, 1)
d  = E(c)                                // display-referred from here down; d ∈ [0,1]
e  = baseLook(d)        if enabled       // §5.4  maps [0,1] → [0.012, 0.97], no clamp needed
f  = clamp01(e + Brightness/100 · 0.35)
h  = clamp01(0.5 + (f − 0.5) · slope)    // slope = tan(π/4 · (1 + Contrast/100 · 0.6))
s  = h + Shadows/100 · 0.35 · h·(1−h)³   // §5.2  closed on [0,1], no clamp needed
t  = clamp01(s + max(Highlights,0)/100 · 0.30 · s³)   // §5.3
u  = master(channel(t))                  // §5.5  input already ∈ [0,1]
lut[i] = clamp01(u)
```

`EVsource` is `BaseImageInfo.SourceExposureBiasEv`, estimated at RAW decode time
(DECODE.md §2.2). Standard images always use 0.

Everything upstream of user curves must stay monotone unconditionally; clamps preserve
that order, and monotone user curves preserve the composed table. `ToneLutApplicator`
interpolates unrounded input and writes Q16 once; standard identity approximates
`E(i/65535)`, round-tripping an unedited JPEG within the tested 8-bit code bound.

### 5.1 Crossing-off highlight shoulder (Highlights H ∈ [−100, 0])

```
k = 1 + H/100 · 0.55                     // knee ∈ [0.45, 1]
shoulder(x, k) = x                        for x ≤ k
               = k + (1−k)·tanh((x−k)/(1−k))   for x > k   (when k < 1)
               = min(x, 1)                     (when k = 1)
```

C1-continuous at the knee, strictly monotone, asymptote 1.0. At `H = 0` this is
identity-then-clip, so unedited images are unaffected.

### 5.2 Crossing-off shadows (S ∈ [−100, 100], display domain)

`x + S/100 · 0.35 · x(1−x)³` — zero at both ends, peak effect near x ≈ 0.25.
On [0,1] it is monotone for the full slider range (|d/dx x(1−x)³| ≤ 1) and its output
stays inside [0,1] (monotone with fixed endpoints 0 and 1). These properties do **not**
hold outside [0,1] — hence the clamp before this step.

### 5.3 Crossing-off highlights, positive side (H ∈ (0, 100])

`x + H/100 · 0.30 · x³` — monotone on [0,1]; output can reach 1.3, hence the clamp
after. The negative side is §5.1's knee.

### 5.4 Crossing-off base look

The optional display-domain base look is:

```
baseLook(x) = x + 0.012(1−x)³ − 0.10·sin(2πx)·4x(1−x) − 0.03x³
```

Monotone on [0,1] (derivative ≥ 1 − 0.63 > 0) and range ⊂ [0,1].
`EditSettings.BaseLook == null` means off. Persisted true/false values remain functional
for crossing-off sources; crossing-on sources retain the value but ignore it.

### 5.5 User curves

Each channel's optional 256-entry `CurveData` precedes the required master table: `u_c =
master(channel_c(t_c))`. Missing channel tables are identity. Both tone regimes share
this seam, before RAW's channel-mixing AgX outset. Identity channels share the master
LUT at no application cost.

Curve tables interpolate at `t·255`. [CurveData.cs](../../Models/CurveData.cs) orders
X but permits decreasing Y for intentional solarization; monotonicity properties
therefore use identity/monotone curves (TESTING.md §4.1).

## 6. Chroma stage

The chroma pass decodes display Rec.2020, transforms through OKLab/OKLCh, then returns
through the inverse chain with one Q16 write. Eight mixer bands use complementary
half-cosine windows summing to one, including the wrap; their hue centers are calibrated
to the UI swatches: Red 24°, Orange 56°, Yellow 105°, Green 146°, Aqua 195°, Blue 266°,
Purple 304° and Magenta 341°.

All band values are sampled simultaneously from the source hue. With the existing
hue-reliability ramp `r(C)` (zero through C=0.01, one from C=0.04), mixer offsets are:

```
Δh = r(C) · Σ wi(h) · Huei / 100 · 30°
bandSat = 1 + r(C) · Σ wi(h) · Saturationi / 100
ΔL = r(C) · Σ wi(h) · Luminancei / 100 · 0.20
(Lm, Cm, hm) = (clamp01(L + ΔL), C · bandSat, wrap(h + Δh))
```

Hue, Saturation, and Luminance are each −100..100. The reliability factor fades all
three aggregates to identity, so achromatic and hue-unreliable pixels take no band
edit. Uniform saturation on all eight bands is therefore equivalent to the global
Saturation slider wherever hue is reliable. Global saturation and vibrance compose
after the band offsets:

```
sat = (100 + Saturation) / 100
vib = 1 + Vibrance / 100 · 0.5 · weight(C, h)
C' = Cm · sat · vib(Cm, hm)
```

`weight(C,h)` is one at zero chroma, tapers smoothly toward zero as chroma grows,
and is further damped by a smooth periodic window centered on the OKLab skin-hue
region. The same weight applies to both vibrance signs. Hue damping fades in only as
hue becomes reliable near the achromatic axis. Saturation −100 sets C exactly to zero.

An inverse result outside linear Rec.2020 [0,1] is projected to the maximal feasible
chroma on the post-edit `Lm`/`hm` ray. The normal path solves the channel-boundary
cubics and retains bounded bisection as a fallback; it never clips channels
independently.

The pass runs in bounded pooled bands, preserves alpha and extra channels, and checks
the resting execution contract for worker limits and cancellation. S=V=0 returns
before pixel access only when the mixer is also pixel-inactive. All transform math is
`double`; transfer lookup interpolation and the final Q16 write are the only
production precision boundary. The reference `(L,C,h) -> (L,C,h)` seam and fused Q16
hot path apply identical mixer ordering.

### 6.1 True monochrome RAW

`BaseImageInfo.IsMonochrome` retains the RAW path with identity WB, no DCP HueSat or
R/G/B curves, and no chroma pass (Saturation, Vibrance, mixer). Stored color settings
remain dormant. The composite curve, exposure/tone, geometry, detail, effects, scopes,
conversion and export stay shared. Preview and sRGB/P3 lossless exports must preserve
exact `R = G = B`.

## 7. Histogram & clipping

`Options.ComputeStats` enables preview-scale statistics with existing 8-bit histogram
bins. Display-floor statistics use finalized display pixels; highlights project the
loader's source-saturation artifact through geometry and resize, unaffected by tone,
color, profile or effects. `PreviewService` passes it from the current
`PreviewBaseLease.Analysis`; `BaseImage` never owns it.

When analysis or preview pixels are requested, the finalizer returns the final encoded
Q16 array with its channel layout. One parallel pass derives display-floor counts,
overlay flags and one BGRA8 buffer without Magick read-back; channels, including alpha,
scale by `(q + 128) / 257`, and absent alpha is 255. Direct finalizer callers and resting
renders do not retain that array.

That BGRA8 buffer supplies both the preview bitmap and display scopes. Histogram-only
ticks skip waveform accumulation; Browse thumbnails never create waveform data.
Render-pipeline accumulation uses one worker per 8192 pixels, bounded by rows and
processors; cached and adjacent-warm paints keep the two-worker cap (one below 512 × 512).
Per-worker integer partials keep merges exact. RAW histograms instead sample the visible
unpacked mosaic before WB, demosaic, characterization and tone (DECODE.md §2), merging
both green phases.

For photosite value `v` and native channel `ch`, RAW binning uses
`black_ch = black + cblack[ch] + repeatingBlock`,
`n = clamp01((v - black_ch) / max(1, maximum - black_ch))`, then
`round(E(n) * 255)` with §3's sRGB encode via a bounded lookup (no `Math.Pow` in the
visible pass). RAW clipping is the separate linear test `v >= maximum`, counted per
sensor channel and written into the spatial source-saturation artifact in the same pass
— never inferred from bin 255. Both green CFA phases merge into the green artifact
plane.

`ClippingStats` carries per-channel high/low fractions, `HighAny`, `LowAll`, and
`IsHighAvailable`. RAW high uses the sensor predicate above. JPEG/HEIC high uses
`sample / encodedMaximum >= 253 / 255` before normalization (1015/1023 at 10-bit).
TIFF, PNG and other standard formats lack that artifact; their floor analysis remains
available and never substitutes a finalized-output high threshold.

Projection follows the forward direction of the exact map carried by the geometry
trace, then crop and final resize. Every downscale OR-reduces source flags so
an isolated set bit survives. A per-base single-entry geometry cache reuses the packed
projection and its fractions across render-only edits. High and floor overlay bits are
ORed independently, allowing one pixel to carry both. Develop requests masks only
while the `J` latch or a triangle peek is active; ordinary preview renders remain
mask-free.

## 8. EditSettings v4 — schema and storage

JSON document shape; `EditSettingsJson` owns canonical serialization for hashing:

```jsonc
{
  "version": 4,
  "exposure": 0.0,                       // EV
  "wb": { "mode": "asShot",              // asShot | custom | preset | picked
          "kelvin": null, "tint": null,  // custom/preset
          "gains": null,                 // [r,g,b] for picked
          "preset": null },              // preset name when mode == preset
  "highlights": 0, "shadows": 0,         // engine params RAW; §5.1–5.3 standard
  "brightness": 0, "contrast": 0,        // brightness ignored RAW; contrast re-anchored
  "saturation": 0, "vibrance": 0,
  "baseLook": null,                      // null = source-kind default
  "hlReconstruction": "clip",            // raw only: blend | clip  (decode-affecting)
  "detail": { "captureSharpen": null,    // null = default (raw 25, else 0); 0-100
              "luminanceNr": 0,           // 0-100
              "chromaNr": 0 },           // 0-100
  "effects": { "vignette": 0,            // optional; -100..100
               "midpoint": 50,            // 0..100
               "grain": 0,                // 0..100
               "grainSize": "medium" },   // fine | medium | coarse
  "lens": { "distortion": true,
            "chromaticAberration": true,
            "vignetting": false },
  "rotation": 0, "horizon_rotation": 0.0, "crop": null,
  "curve": { },
  "curveRed": { },                       // optional; omitted = identity
  "curveGreen": { },                     // optional; omitted = identity
  "curveBlue": { },                      // optional; omitted = identity
  "applied_preset_id": null,
  "rawProfile": { "source": "userFile", // omitted for built-in
                  "location": "C:\\Profiles\\Camera.dcp",
                  "contentHash": "<lowercase SHA-256>" },
  "mixer": {                              // optional; omitted at identity
    "red": { "hue": 0, "saturation": 0, "luminance": 0 },
    "orange": { "hue": 0, "saturation": 0, "luminance": 0 },
    "yellow": { "hue": 0, "saturation": 0, "luminance": 0 },
    "green": { "hue": 0, "saturation": 0, "luminance": 0 },
    "aqua": { "hue": 0, "saturation": 0, "luminance": 0 },
    "blue": { "hue": 0, "saturation": 0, "luminance": 0 },
    "purple": { "hue": 0, "saturation": 0, "luminance": 0 },
    "magenta": { "hue": 0, "saturation": 0, "luminance": 0 }
  },
  "geometry": { "vertical": 0,           // optional; omitted at identity
                "horizontal": 0,
                "aspect": 0,
                "distortion": 0 },
  "locals": [{                           // optional; omitted when empty
    "id": "<32-hex GUID>", "type": "radial", "ordinal": 1, "enabled": true,
    "cu": 0.5, "cv": 0.5, "angle": 90, "feather": 0.25, "exposure": 0,
    "rx": 0.25, "ry": 0.25, "outside": false, // radial only
    "temperature": 10, "tint": 5, "saturation": 20, // omitted when zero
    "luminance": { "enabled": true, "lower": 0, "upper": 1, "softness": 0.1 },
    "hue": { "enabled": true, "center": 240, "width": 60, "softness": 30 }
  }]
}
```

Local coordinates belong to the corrected frame after quarter-turn and warp, before
crop, and use its long edge as the metric. For linear locals, angle zero points right
and positive angles turn clockwise. Weight is
`1 - smoothstep((s + feather/2)/feather)` for signed distance along that direction.
For radial locals, rotate the center offset by minus angle and compute
`rho = sqrt((x/rx)^2 + (y/ry)^2)`: inside weight is one below `1-feather`, zero at
or beyond one, and inverse smoothstep between; zero feather gives a hard edge.
Outside complements that weight. Quarter-turns carry centers and angles with the
photograph while preserving radii and feather; clockwise 90° maps the center to
`(1-cv, cu)`. The resting frame override preserves this coordinate system.

Brush locals use `"type": "brush"` and `"strokes": []`, with no gradient geometry
(`cu`, `cv`, `angle`, local `feather`, `rx`, `ry`, or `outside`). Each ordered stroke
stores `{"mode":"paint","radius":0.03,"feather":0.5,"flow":1,"points":[8192,8192,128,-64]}`.
Mode is `paint` or `erase`. Radius clamps to [0.001, 0.25] in long-edge units,
feather to [0, 1] and flow to [0.05, 1]. Points are paired integer deltas at
1/16384 per corrected axis: the first pair is absolute, later pairs accumulate
from the previous point. Reconstructed coordinates clamp to [-1, 2]. In memory,
points are absolute quantized integers. Immutable, value-equal arrays of points
and immutable strokes are safely shared by model copies. A clockwise quarter-turn
maps every point to `(16384-v, u)` exactly.
Empty brushes are valid; missing or malformed strokes reject the document.
Gradient locals omit strokes; a non-null strokes array on a gradient is rejected.
Unknown stroke fields are ignored. Across all brush locals, more than 96 strokes or 4,000 points
rejects the document, including disabled and neutral locals.

Brush weight starts at zero. For each stroke, distance `d` is the minimum distance
to its polyline in the aspect-aware long-edge metric; a single point is a disc.
Stroke weight `s` is flow inside `r(1-f)`, zero at or beyond `r`, and
`flow * smoothstep((r-d)/(r*f))` between them (zero feather is a hard edge).
Segments of one stroke never accumulate coverage. Paint composes as
`c += (1-c)*s`; erase as `c *= 1-s`, in stroke order. Range windows multiply the
result exactly as for gradients. `LocalBrushEvaluator` builds a double CSR segment
grid per render, fitted to expanded segment bounds with roughly r/2 cells,
stroke bounds culling and saturation exits. The 1 MiB index/construction budget is
shared across the document: reserve caller setup and each brush's one-cell minimum,
then divide the remainder by segment count. Scratch arrays are bounded before allocation;
coarsening always accepts a one-cell grid. No pixel-sized mask plane is retained.
`RenderLocals` maps crop/scale and resting frame overrides into the same corrected
coordinates on both Gain and color/range paths. Requested brush masks use this
weight through LocalRangeMask, with stroke content in its identity. Unrestricted
brush masks use a numeric frame without acquiring a range base or reading pixels;
restricted masks retain the matching loaded base. During painting, both request and
currency checks use the gesture's before-snapshot, allowing one pending pre-stroke
mask to publish while live points only update the ribbon and throttled preview.
Release unpins and refreshes the mask; discard/navigation cancel pending work so
old results cannot publish. Updating mask follows the existing requested-mask path.

`EditSettingsJson` validates finite values, local identities/types and the eight-local
limit. Disabled and neutral locals remain stored edits. Range disabling preserves its
values; omitted ranges and zero color terms preserve canonical exposure-only bytes.
Brush-free canonical v4 bytes and the render version are unchanged.
Version 3 settings migrate in memory without locals; version 2 is unsupported.

| Field | Omitted when | Copy/paste and presets | Decode-affecting? |
|---|---|---|---|
| Channel curves | Absent | Transfer | No |
| `mixer` | All bands zero | Transfer | No |
| `effects` | Vignette and Grain inactive | Transfer | No |
| `geometry` | All terms zero | Preserve destination, as with crop/rotation | No |
| `rawProfile` | Built-in | Preserve destination | Yes |
| `detail` | Always present; null sharpening means source default | Transfer | No |
| `lens` | Always present; override omitted when null | Transfer booleans; preserve override | Yes |
| `locals` | Empty | Preserve destination; excluded from preset files | No |

Highlight reconstruction also affects decode. Lens settings are defined in OPTICS.md.
Identity mixer/effects objects canonicalize to null; selecting a UI band or inactive
effect option does not materialize a persisted edit. Global Reset clears image-specific
profile, geometry, and locals state; UI.md §6 owns transfer and undo behavior.

### 8.1 Catalog storage ([CatalogSchema.cs](../../Services/CatalogSchema.cs))

The read path is row-local and never writes; [ARCHITECTURE.md](../ARCHITECTURE.md)
owns catalog schema, migrations, and write contracts.

### 8.2 Current-format boundaries

`EditSettingsJson` serializes a validated clone of the current model without mutating
its caller. Presets require explicit supported wrapper/settings versions and load
without rewriting files. Copy/paste requires current in-memory source and target models.

## 9. Detail stage

The shared detail stage applies luminance NR, chroma NR, then capture sharpen.
Source defaults and serialized fields are defined by `DetailSettings` (§8).

Spatial support scales as `σ_effective = σ_native · renderLongEdge / max(Info.FullWidth,
Info.FullHeight)`. Export capture sharpen skips sub-0.3px support; Preview/resting floor
it at 1.0 screen px, matching Lightroom's native default radius so Fit and the 3200px
base share one sigma without a sharpening pop. Fit deliberately overstates sharpening to
keep the control judgeable; export-scale renders remain the detail reference.

- **Luminance NR:** four native à trous/starlet scales use B3-spline taps
  `[1 4 6 4 1]/16`. Scale support `2^s` by render/native long edge, discard sub-0.3px
  support, round to a dyadic octave and discard invalid/oversized supports.
  Thresholds are `6200 · [0.8907963, 0.2006639, 0.0855075, 0.0412175] · v/100` Q16,
  log-linearly evaluated at bounded fractional indices before spatial quantization.
  Soft-thresholded detail reconstructs a luma delta added equally to RGB after clamping
  to `[−min(R,G,B), 65535−max(R,G,B)]`, preserving chroma and alpha at gamut boundaries.
- **Capture sharpen:** luminance-targeted unsharp, `σ_native=0.75`,
  `amount=v/100`, `threshold=0.01`, with the intent-specific sigma rule above.
- **Chroma NR:** five scales denoise `Cb=B−Y` and `Cr=R−Y` using
  TONE_ENGINE.md §6's luma authority and the same support/B3-spline engine.
  Thresholds are `6500 · [0.90, 0.25, 0.12, 0.08, 0.05] · v/100` Q16.
  The finest scale stays full-size; deeper scales downsample by two, apply dilation-one
  taps and upsample the adjustment. A finest-scale cross-plane gradient over four
  thresholds uses 0.35× threshold, preserving edges independently of orientation.
  Reconstruction retains Y and scales the complete RGB delta just enough for gamut;
  green quantization preserves authoritative quantized Y, with alpha unchanged.
  Monochrome skips this stage.

Both NR planes share source-band reads/workspace; full summed halos make banding
bit-identical to a single band. Zero values or no surviving scales return before pixel
access. Interactive, resting and export share the post-tone, pre-sharpen order.
Output sharpening is defined in OUTPUT.md §3.

## 10. Effects substep of output finalization

`RenderEffects` runs one skipped-when-inactive pass on encoded display Rec.2020 after
resize/output sharpening and before target conversion. Preview, resting and each export
variant share vignette then grain, at the variant's output dimensions. Thumbnails are
non-authoritative for effects and may resample grain; Develop preview and export are
authoritative.

Vignette uses a smooth elliptical falloff in normalized post-crop coordinates: negative
multiplies toward black, positive lifts toward white, and Midpoint sets onset. Output
dimensions do not change it. During Crop, Develop renders the full pending canvas for
overlay alignment; vignette recenters on the committed crop when Crop exits.

Grain adds an equal-channel, amount-scaled delta from a stateless `(x, y, grainSize)`
hash in encoded display space. Fine hashes each pixel; Medium/Coarse bilinearly
interpolate fixed 2px/3px cells. Clamping the shared delta to `[−min(R,G,B),
1−max(R,G,B)]` preserves channel differences at gamut boundaries; alpha is untouched.
Frequency uses output pixels, so preview/export appearance agrees without sample
identity across resolutions.

## 11. Performance contract

State-defining outcomes carry pixels, active scopes and matching source facts together.
VM acceptance requires the current image and surface generation; stale/failed outcomes
cannot replace facts or promote thumbnails. Entry seeds both display scopes; histogram
ticks retain the last waveform until a coherent waveform render replaces it. A
settings-matched cached BGRA buffer supplies bitmap, scopes and floor statistics without
source access; mismatches are bitmap-only. Caches never claim RAW/high facts.
Discovering that the selected original needs hydration advances the surface generation
and clears even a provisional cache paint.

Promotion is committed only after VM acceptance of a current, committed edited RAW
outcome. Rejected or speculative outcomes dispose their lease; promotion never waits
on background work. Shutdown awaits candidate queueing before draining the cache writer.

A 1600px slider tick runs geometry, optional HueSat, fused matrix → LUT → matrix, then
optional OKLCh/detail. The development budget is ≤150 ms (`HAPPY_PHOTON_PERF=1`); exact
tone tables are cached for a bounded active settings set. Active chroma, including
pixel-cache traffic on a projection-heavy S=+100 fixture, also has the 60 ms
AgX-crossing budget; identity chroma requires zero pixel access. Tonal/chroma/geometry
sliders invalidate only render; only `BaseDecodeSettings` invalidates the base.

Effects-off finalization returns before pixel access and adds no work. On the opt-in
Release fixtures, active effects retain the ≤150 ms preview-tick budget
(active-minus-off delta ≤25 ms), full export delta is ≤max(5%, 500 ms), incremental
private-memory peak is at most one processed Q16 RGB frame, and resting cancellation
is observed at the next effects execution check.

After an accepted interactive paint, a display-only resting render may use the large
base at the crop-aware view's required device-pixel size, capped by that base and 3200.
Fit/zoom-in request refinement; pan/zoom-out do not. Geometry and linear resize precede
detail so it sees achievable scale. The render uses the same math but no statistics,
thumbnail promotion or disk writes. A resting serial, captured interactive generation
and decode key reject stale results without advancing interactive generation.
Cancellation is checked between native operations; resting managed kernels use at most
two workers and remain bit-identical across worker caps. Zoom beyond the large base
stretches available pixels until native region decode exists.

DCP latency, allocation, and discovery budgets live in `DcpPerformanceGateTests`
(TESTING.md §5); an inactive profile adds no work.

`HAPPY_PHOTON_DISPLAY_TRACE=1` enables post-layout size, scale, bitmap provenance, and
device-true 1:1 diagnostics. It is captured at startup; when off no observer is
installed. The WinExe has no reliable console capture; use `%LOCALAPPDATA%\Happy
Photon\logs\display-trace.log`, truncated at process start.
