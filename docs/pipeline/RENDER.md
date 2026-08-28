# Render: `BaseImage` × `EditSettings` to Pixels

`RenderPipeline` is the single pixel path shared by preview, histogram, and export.
The formulas below explain how it preserves linear-light headroom while reducing the
tonal work to one quantization step. All Magick.NET processing remains Q16.

## 1. Stage order (fixed)

```
1 Geometry     rotate90 → fused horizon/keystone/aspect/radial warp → crop
2 DCP HueSat   optional scene-linear ProPhoto HSV profile map (§2.1)
3 Matrix       crossing on: AgX inset × WB; crossing off: WB (§4)
4 Tone LUT     source-kind tone regime, fused with matrix storage (§5)
5 Matrix       crossing on: AgX outset; crossing off: identity
6 Chroma       one fused OKLCh color-mixer/saturation/vibrance pass (§6)
7 Detail       luminance NR → capture sharpen → chroma NR (§9)
8 Output       linear resize → output sharpen → effects → target convert → encode
               (OUTPUT.md)
```

`RenderGeometry` owns one clone of the immutable base. Quarter turns remain a separate
lossless operation. Any active horizon or manual geometry term then runs in one
inverse-mapped bilinear pass; identity skips that pass. The corrected frame preserves
the quarter-turned source aspect and is reduced, never upsampled, to the largest
centered frame whose mapped boundary is covered by source pixels. Crop coordinates are
normalized on that corrected frame, so both a null and an explicit full-image crop are
blank-free.

Centered keystone coordinates use `w = 1 + a·y + b·x`, with Vertical and Horizontal
slider values mapping to `a,b = −value/200`. Aspect applies `sx=e^s`, `sy=e^−s`,
`s=value/400`. Manual radial is destination-to-source
`f(ru)=ru·(1+k·ru²)`, `k=−value/400`, where `ru` uses the source half-diagonal.
Beyond `ru=1`, `f` continues linearly with value `1+k` and slope `1+3k`; the forward
projection uses the closed-form inverse of the cubic below that knee and division
above it. This keeps the map monotone at every slider setting.

### 1.1 Request contract

```csharp
public sealed record RenderOptions(
    bool ComputeStats = true,
    bool ComputeOverlayMasks = false,
    ClippingOverlaySide OverlaySides = ClippingOverlaySide.Both);
public sealed record RenderRequest(
    BaseImage Base, EditSettings Settings, RenderIntent Intent,
    int? MaxDimension, RenderOptions Options,
    OutputColorSpace OutputColorSpace = OutputColorSpace.Srgb,
    OutputSharpeningMode OutputSharpening = OutputSharpeningMode.Off);
```

- `OutputColorSpace` selects sRGB (default) or Display P3 only in finalization. Preview
  always forces sRGB with output sharpening off. The Export workspace's opt-in proof is
  a distinct `RenderDisplayRec2020` plus proof-finalization path, not an exception to
  the Preview intent contract. Geometry, tone, chroma, and detail are target-independent;
  `Intent`, `Options`, and `MaxDimension` otherwise change auxiliary work such as
  statistics, optional overlay masks, and the resize target.
- **Base immutability:** `RenderPipeline` never mutates `Base.Pixels`;
  `RenderGeometry.Apply` always returns the single owned clone/output used by later
  stages. `BaseImage` lifetime is owned by the caller
  (`PreviewService` generation logic / export loop), never by the pipeline.
- **Resize domain:** every downscale — preview `MaxDimension` and export variants —
  runs in linear light with the same filter (Magick default Lanczos): sRGB-decode →
  `Resize` → sRGB-encode (the preview *base* is already linear and is resized before
  encoding). Honest limit: preview resizes the neutral base *before* tone mapping,
  export resizes the rendered result *after* it, and tone curves/clamps do not commute
  with resampling — the two paths are a deliberate, performance-driven approximation
  of each other, governed by the WYSIWYG ΔE bounds (TESTING.md §3, row 3).

## 2. Why matrix → single LUT → matrix

The chromatic part of WB and the AgX inset/outset are 3×3 matrices, not per-channel
curves; everything between them is a per-channel 1D function. `AgxCrossing` evaluates
inset → exact 65,536-entry interpolated tone table → outset in `double`, then makes
one Q16 write. Crossing-off degenerates to WB → retained display chain → identity.
This avoids clipped intermediates and cumulative requantization while keeping slider
ticks bounded to one fused pass plus optional chroma/detail work.

### 2.1 DCP HueSat stage

An active DCP HueSat map runs after geometry and before the AgX inset. The payload
comes only from the installed base, so it always matches the profile matrix used
during decode. The transform is working Rec.2020 D65 → linear ProPhoto D50 → HSV →
profile map → linear ProPhoto → working space. For sRGB table encoding, only HSV V is
encoded before lookup and inverse-decoded after; H and S remain linear.
ValueDivisions=1 is a 2.5D hue/saturation lookup and ignores the encoding tag. Dual
tables share decode's as-shot interpolation weight; single tables do not vary with it.
A 65³ Q16 RGB lattice compiled from that sequence (cached process-wide by profile
content and weight) is trilinearly evaluated as a pass fused onto the AgX crossing's
whole-frame working array — one read and one write total, in both the interactive and
resting render paths. With no active table the crossing runs its unmodified math,
preserving exact no-profile output.

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

RAW sources use the scene-referred crossing defined normatively in
[TONE_ENGINE.md](TONE_ENGINE.md): exposure gain → normalized log2 → parameterized
sigmoid → `u^2.2` → sRGB encode → channel curve → master curve → decode →
AgX outset → encode.
Contrast controls sigmoid slope at grey, Highlights controls the shoulder power, and
Shadows controls the toe power. The post-gain scene value `a = 0.18` maps to the pinned
display grey for every Contrast value and every `EVsource`. Brightness and base look
are ignored in this regime.

Standard sources retain the display-referred chain below. There is no automatic
exposure trigger and no persisted crossing toggle.

`ToneLut.Compose(ToneParams p) → ToneLuts` is pure and unit-testable. `ToneLuts`
carries three per-channel `double[65536]` arrays; a channel without its own curve
shares the master array. Entry `i`
uses `v = i/65535`, a linear post-matrix value. Display-domain operators are defined
on [0,1] only. The marked `clamp01` calls keep the §5.2/§5.3 polynomials inside their
monotone domains; without those clamps the chain can break (for example, Contrast
+100 can push values to ≈ 3.1 before clamping):

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

Each step is monotone non-decreasing on its domain and `clamp01` preserves (non-strict)
monotonicity, so the composed LUT is non-decreasing whenever the user curve is —
flat plateaus from clamping are expected and legal. Apply with
`ToneLutApplicator`, which linearly interpolates the exact 65,536 entries on the
unrounded matrix result and writes Q16 once.
The identity settings vector must produce, for non-raw bases,
`lut[i] ≈ E(i/65535)` — a JPEG with zero edits renders back to its original appearance
within 1 LSB at 8 bits (regression test).

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

Exact port of the current decode-time curve, now optional and float:

```
baseLook(x) = x + 0.012(1−x)³ − 0.10·sin(2πx)·4x(1−x) − 0.03x³
```

Monotone on [0,1] (derivative ≥ 1 − 0.63 > 0) and range ⊂ [0,1].
`EditSettings.BaseLook == null` means off. Persisted true/false values remain functional
for crossing-off sources; crossing-on sources retain the value but ignore it.

### 5.5 User curves

Each channel optionally has a `CurveData` 256-entry table, followed by the required
composite/master table: `u_c = master(channel_c(t_c))`. A missing channel table is
identity. Both tone regimes compose this at one shared seam; RAW keeps it before the
AgX outset, whose matrix may then mix channels. Identity channel curves share the
master LUT array and add no application cost.

Tables are evaluated with linear interpolation between entries at LUT-composition
time (input `t·255`). `CurveData` orders points by X but does **not** constrain Y
([CurveData.cs](../../Models/CurveData.cs)) — a user can draw a decreasing curve, and
that is allowed (deliberate solarization is user intent, not a pipeline bug).
Consequently the global monotonicity *property test* runs with identity/monotone
curves only (TESTING.md §4.1); everything upstream must stay monotone unconditionally.

## 6. Chroma stage

The encoded display-Rec.2020 value is decoded, converted through linear Rec.2020 to
OKLab/OKLCh, transformed, returned through the inverse chain, encoded, and written
once to Q16. The eight mixer bands are Red, Orange, Yellow, Green, Aqua, Blue,
Purple, and Magenta. Adjacent band centers are joined by complementary half-cosine
windows, including the Magenta→Red wrap, so the periodic weights are smooth and sum
to one at every hue. Their OKLab hue centers, calibrated to the UI swatches, are
Red 24°, Orange 56°, Yellow 105°, Green 146°, Aqua 195°, Blue 266°,
Purple 304°, and Magenta 341°.

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

`BaseImageInfo.IsMonochrome` keeps true monochrome sources on this same RAW render
path while making color settings dormant. Rendering uses identity WB, omits the DCP
HueSat map and R/G/B channel curves, and skips the fused chroma pass entirely —
Saturation, Vibrance, and the color mixer. The
composite curve, exposure and tone engine, geometry, detail, effects, scopes, output
conversion, and export remain shared. Persisted color settings are neither applied nor
cleared. Exact `R = G = B` is required through preview and both sRGB and Display P3
lossless exports.

## 7. Histogram & clipping

Computed at preview scale when `Options.ComputeStats` (existing `HistogramService`
bins stay 8-bit). Display-floor statistics sample the finalized display. Highlight
statistics come from the loader-produced source-saturation artifact projected through
the render geometry and final resize; tonal, color, profile, and effect math never
redefines those flags. `PreviewService` passes that artifact explicitly from the
current `PreviewBaseLease.Analysis` on the render request; `BaseImage` does not own it.

The render exports one BGRA8 buffer that is both the preview-bitmap source and the
display-scope source. Histogram-active interaction accumulates only the four 8-bit
histogram channels; waveform-active interaction also accumulates the 256-column ×
128-level luminance waveform. Horizontal image position maps to columns, Rec.601
luminance maps with `level = value8 >> 1`, and each `ushort` cell stores the sample
count. Sources narrower than 256 pixels back-fill unrepresented columns. Browse
thumbnail histograms use the bitmap overload and never create waveform data.

The selectable RAW histogram is deliberately outside that render stage: `RawBaseLoader`
captures it from LibRaw's preserved post-`Unpack` mosaic before output configuration,
white balance, demosaic, camera conversion, highlight reconstruction, and tone
(DECODE.md §2), then installs it with the matching preview pair's source analysis. It
walks only the visible window at `top_margin + row`,
`left_margin + column` with stride `raw_pitch / sizeof(ushort)`; CFA phase and
repeating black blocks use visible coordinates, and both green phases merge into green.

For photosite value `v` and native channel `ch`, RAW binning uses
`black_ch = black + cblack[ch] + repeatingBlock`,
`n = clamp01((v - black_ch) / max(1, maximum - black_ch))`, then
`round(E(n) * 255)` with §3's sRGB encode via a bounded lookup (no `Math.Pow` in the
visible pass). RAW clipping is the separate linear test `v >= maximum`, counted per
sensor channel and written into the spatial source-saturation artifact in the same pass
— never inferred from bin 255. Both green CFA phases merge into the green artifact
plane.

```csharp
public sealed record ChannelClip(double R, double G, double B);   // fractions 0..1
public sealed record ClippingStats(
    ChannelClip High,        // aligned source-saturation fraction per channel
    ChannelClip Low,         // per channel: fraction ≤ 0.5/255
    double HighAny,          // aligned source-saturated pixels, any channel
    double LowAll,           // all-channels-low fraction (drives the blue overlay/chip)
    bool IsHighAvailable);   // artifact capability; independent of source-kind gates
```

RAW `High` uses the histogram's exact sensor predicate. JPEG/HEIC use the decoded
encoded-sample ratio `sample / encodedMaximum >= 253 / 255` before ICC/EOTF
normalization (253/255 for 8-bit; 1015/1023 for 10-bit). TIFF, PNG, and other standard
formats have no v1 source artifact, so only their high side is unavailable; floor
analysis remains live and never falls back to a finalized-output high threshold.

Projection follows the forward direction of the exact map carried by the geometry
trace, then crop and final resize. Every downscale OR-reduces source flags so
an isolated set bit survives. A per-base single-entry geometry cache reuses the packed
projection and its fractions across render-only edits. High and floor overlay bits are
ORed independently, allowing one pixel to carry both. Develop requests masks only
while the `J` latch or a triangle peek is active; ordinary preview renders remain
mask-free.

## 8. EditSettings v3 — schema and storage

JSON document shape (canonical field order for hashing):

```jsonc
{
  "version": 3,
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
            "vignetting": false,
            "baseline": "standard" },     // standard | legacy
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
                "distortion": 0 }
}
```

The three channel fields follow `curve` in the shown order; they and `rawProfile` use
null-omission semantics — `null` is never serialized, and `Clamp` validates/rebuilds
an optional curve only when the field was present. Selecting a channel in the UI does
not materialize it. A legacy v2 document instead follows the explicit lens-baseline
upgrade described in §8.1.

`mixer` is omitted unless at least one of its 24 values is nonzero. That same
pixel-activity predicate governs `HasEdits`, hashing, and the chroma-stage skip;
`EditSettingsJson` in both directions and preset saving canonicalize an explicit
identity mixer to null. Clone/history, copy/paste, and presets carry active mixers.
The selected mixer band is session view-state and is never serialized. Mixer values
have no XMP mapping. Because absent mixers preserve canonical bytes and pixels, this
additive optional field does not change `RenderPipeline.Version`.

`effects` is omitted when neither Vignette nor Grain changes pixels; that
`HasActivePixels` predicate governs persistence, `HasEdits`, hashing, and the render
skip, and `EditSettingsJson` and preset saving canonicalize an explicit pixel-inactive
object to null. Midpoint and Size choices made while both operators are off remain
session-only UI state. Effects-off pixels stay byte-identical, so this additive
optional field does not change `RenderPipeline.Version`.

`geometry` is omitted when all four values are zero. It is catalog-only and has no XMP
payload. Clone/history and Reset include it, while copy/paste and presets exclude it
with rotation, horizon, and crop. Although absence is identity, the corrected-frame
change alters horizon pixels, so this feature increments `RenderPipeline.Version`.

`hlReconstruction`, the three `lens` booleans, and `rawProfile` are the
**decode-affecting subset**;
they project into `BaseDecodeSettings` (OVERVIEW.md §4, DECODE.md §4) and changing
them re-decodes the base rather than re-rendering it.

The profile field is additive v2. Clone/history retain it, global Reset clears
it, and it contributes to `HasEdits`. Preset hover/apply/untoggle preserve it;
preset files and copy/paste exclude it because it is camera- and
file-specific. Omitting built-in preserves legacy canonical JSON and hash identity.

Capture sharpening resolves a `null` value to RAW 25 or standard 0 and canonicalizes
the matching default back to `null`. Luminance and chroma NR are always-serialized
0–100 values for every source. Legacy `detail.noiseReduction` input is ignored without
rewriting the stored document (§9, UI.md §2). Preview detail uses the bounded preview base, while
export-scale renders are the fidelity reference.

### 8.1 Catalog storage ([CatalogSchema.cs](../../Services/CatalogSchema.cs))

The canonical `images` table contains `id`, `file_path`, `file_name`, `edit_settings`,
`edit_version`, `flag_state`, `rating`, `color_label`, and `updated_utc`. New rows
always receive a complete v3 JSON document and `edit_version = 3`.

`CatalogSchema` creates the tables for a new catalog, runs the ordered transactional
`CatalogMigrations` recorded by `app_settings.schema_version`, then validates the
required columns of `images` and `image_assessments` on startup. Extra columns are
tolerated but ignored; a missing required column fails startup with an actionable
instruction to move the entire catalog folder aside before Retry — keeping the folder
intact prevents recycled catalog IDs from resolving to thumbnails or previews belonging
to the incompatible database. The full schema, including migrations and location moves,
is documented in [docs/ARCHITECTURE.md](../ARCHITECTURE.md) ("The catalog").

The read path is row-local and never writes:

- marker 3 + valid document → parse and return;
- marker 2 + valid document → materialize an explicit legacy all-off lens baseline,
  then return v3 settings without writing the row;
- out-of-range current values → clamp in memory and log once;
- null or malformed document, or any other marker → log once and return neutral
  current settings.

One corrupt row therefore cannot fail the folder's batched load. Single and batch edit
writes serialize the complete current document and marker; batch writes retain their
single-transaction all-or-nothing behavior.

The explicit lens baseline marker prevents a legacy image from acquiring standard
defaults after an ordinary save. New images use `standard` (distortion/CA on,
vignetting off); legacy images use `legacy` (all off). Reset restores the image's own
baseline. Copy/paste and preset application transfer only the three values, leaving
the destination baseline untouched.

### 8.2 Current-format boundaries

`EditSettingsJson.Serialize` requires the current v3 model, clones it,
clamps and validates the clone, then writes canonical JSON; it never changes the
caller's model and rejects every other version. Preset files must explicitly declare
their wrapper and settings versions. Current and v2 settings load through the same
legacy lens-baseline migration as catalog rows; versionless or unsupported files are
skipped. Loading never rewrites a preset. Copy/paste accepts only current in-memory
settings and rejects a non-current source or target before applying values.

## 9. Detail stage

The Develop Detail group exposes three sliders (§8, UI.md §2): Sharpen,
Luma NR, and Chroma NR. All apply to every source. Defaults are
capture sharpening 25 RAW / 0 standard and both NR values 0. The
implementations are part of the shared pipeline and covered by parity and performance
tests.

All spatial parameters are **defined at native (full-base) resolution** and scale with
the render: `σ_effective = σ_native · renderLongEdge / max(Info.FullWidth, Info.FullHeight)`
(the native dimensions live on `BaseImageInfo`, set for preview bases too); skip the op
when `σ_effective < 0.3` px (perceptually nil). This keeps preview and export
consistent: at a 1600px preview of a 24MP image, capture sharpening is a deliberate
near-no-op — exactly how its full-res effect survives downscaling. Sharpening is judged
at export size (as in Lightroom); the WYSIWYG goldens compare at preview scale, where
both paths agree by construction.

- **Luminance NR** (0–100): four native à trous/starlet detail scales use the
  separable B3-spline taps `[1 4 6 4 1]/16`. Each native support `2^s` is multiplied
  by `renderLongEdge/nativeLongEdge`; support below 0.3 px is discarded, the remaining
  support is rounded to the nearest dyadic octave, and a quantized index below 1 or a
  support radius beyond one quarter of the shorter render edge is discarded. Thresholds
  are `6200 · [0.8907963, 0.2006639, 0.0855075, 0.0412175] · v/100` Q16 at integer
  scale indices, with log-linear evaluation at the bounded exact fractional index
  before spatial quantization. The mapping is linear across the full slider, with
  6200 as the tuned maximum at 100. Each detail plane is soft-thresholded and
  reconstructed.
  The luma delta is added equally to R, G, and B after clamping it to
  `[−min(R,G,B), 65535−max(R,G,B)]`; Cb/Cr and alpha therefore remain unchanged even
  at gamut boundaries. A zero value or empty surviving scale set returns before pixel
  access. The parallel band kernel carries the full summed halo and is bit-identical
  to a single band. It runs post-tone and before capture sharpen on interactive and
  resting paths; large tone moves may require retuning the slider.
- **Capture sharpen** (0–100, default 25 raw / 0 non-raw): luminance-targeted unsharp,
  `σ_native 0.75, amount = v/100 · 1.0, threshold 0.01`, applied before any resize.
  Luminance-only (Lab L or equivalent); acceptance = no chroma fringing on golden crops.
- **Chroma NR** (0–100): preserve the authoritative Rec.2020 luma
  `Y = 0.2627002120112671R + 0.6779980715188708G + 0.0593017164698620B`;
  blur `Cb = B−Y` and `Cr = R−Y` with an
  edge-clamped separable box, then reconstruct RGB at the original Y.
  `σ_native = v/100 · 2.0`; choose the integer radius
  `r = max(1, round((√(1 + 12σ²) − 1) / 2))`. When its variance `r(r+1)/3`
  exceeds `σ²`, blend the blurred chroma over the original by
  `σ² / (r(r+1)/3)`.
  The implementation is one parallel banded kernel (at most 8 million core pixels per
  band) reading `r+1` halos before writing in place — safe because the preceding tonal
  stage's full-image write already detached the working clone's pixel cache. Band
  partitioning must be bit-identical to a single band.
- **Output sharpen**: OUTPUT.md §3.

## 10. Effects substep of output finalization

`RenderEffects` runs on encoded display Rec.2020 after the final linear-light resize
and optional export output sharpening, immediately before the target conversion: one
skipped-when-inactive in-place pass (`GetArea` → parallel coordinate kernel →
`SetArea`) shared by preview/export finalization and capped-worker resting
finalization. Multi-variant export applies the snapshotted settings after each
variant's progressive resize and sharpen. The internal order is vignette, then grain.

Vignette is an elliptical smooth falloff over normalized coordinates of the post-crop
output frame — negative multiplies toward black, positive lifts toward white, Midpoint
moves the falloff onset — so the field is unchanged by output dimensions. While crop
mode is active, Develop intentionally renders the full pending canvas so the overlay
stays aligned; the vignette previews on that full canvas and recenters on the
committed crop when crop mode exits.

Grain is an equal-channel additive delta in the encoded display domain: a stateless
coordinate hash over `(x, y, grainSize)`, amount scaling the stable signed sample.
Fine hashes every pixel; Medium and Coarse bilinearly interpolate fixed 2px and 3px
cells. The shared delta is clamped to the gamut-safe interval
`[−min(R,G,B), 1−max(R,G,B)]`, preserving channel differences at gamut boundaries;
alpha is untouched. Frequency is defined in output pixels, so preview and export are
appearance-consistent rather than sample-identical across resolutions.

## 11. Performance contract

Preview rendering calculates the active display scope from the exact display-referred
sRGB bytes used for bitmap conversion. Entry paints seed both scopes. Histogram-active
ticks skip waveform accumulation and retain the last trace for an immediate scope
switch; selecting Waveform schedules one current-generation render that replaces it
with a coherent trace. A settings-matched q90 rendered-cache load likewise copies one
BGRA buffer and derives its bitmap, both display scopes, and display-floor clipping
without entering `RenderPipeline` or opening the source; a mismatched cache remains
bitmap-only. Cached results never claim source-saturation clipping or a RAW histogram.
For an edited
RAW whose service render is still current, `PreviewService` places the detachable
`RenderResult.Image` in the outcome's promotion lease. Only VM acceptance of a
committed edited-state render from the current base commits that lease and starts the
tracked resize (linear light, capped at 512px); rejection, stale-base paint, before
view, preset hover, crop draft, cache, resting, and shutdown dispose it without
promotion. The candidate is a cache artifact, not a render stage, so
`RenderPipeline.Version` is unchanged and no full-size clone is made. Promotion never
waits on background work.
Shutdown awaits candidate creation and cache queueing before the writer is drained.

Per slider tick at 1600px preview: geometry (usually no-op), optional profile HueSat,
the fused matrix → tone LUT → matrix pass, then optional OKLCh/detail work.
The development budget is ≤ 150 ms, measured with `HAPPY_PHOTON_PERF=1`; the exact tone
tables are cached for the bounded active settings set. The active-chroma pass —
pixel-cache traffic included, gated on a projection-heavy S=+100 fixture — is
additionally capped at the 60 ms AgX-crossing cost class, and identity chroma is
pinned to zero pixel access. Tonal, chroma, and geometry slider moves invalidate
only the render; only `BaseDecodeSettings` changes invalidate the base.

Effects-off finalization returns before pixel access and adds no work. On the opt-in
Release fixtures, active effects retain the ≤150 ms preview-tick budget
(active-minus-off delta ≤25 ms), full export delta is ≤max(5%, 500 ms), incremental
private-memory peak is at most one processed Q16 RGB frame, and resting cancellation
is observed at the next effects execution check.

After a current 1600 paint settles, the display-only resting entry point may render a
crop-aware snapshot of the large preview base at the active view's required
device-pixel long edge (fit uses the fitted image bound; manual zoom uses the
original-relative zoom times the original-scale displayed geometry). Zoom-in settles a
new render; pan and zoom-out do not. The request is capped by the large base and 3200,
so zoom beyond that base stretches the best available preview until native region
decode exists. Geometry and the linear resize run before the pipeline so
size-dependent detail stages see the achievable resting scale. The result uses the
same render math and version, but skips statistics and is excluded from rendered
thumbnails and disk caches; a separate resting serial plus the captured interactive
generation and decode key reject stale results without advancing the interactive
generation. Resting execution checks its cancellation token between native full-frame
operations; only the resting entry point supplies the optional worker cap (at most two
managed workers) and token to the managed kernels. Ordinary `Render` and every
interactive caller are unchanged, and output is bit-identical regardless of the
resting worker cap — band partitioning does not alter pixel values.

The optional DCP HueSat stage carries its own gates — preview/export deltas,
profile resolution and decode bounds, allocation ceilings, and discovery-scan
bounds — held normatively by TESTING.md §5's opt-in `DcpPerformanceGateTests`,
with exactly zero inactive work.

`HAPPY_PHOTON_DISPLAY_TRACE=1` enables the permanent display-chain diagnostic: the
active Develop or fullscreen preview emits one post-layout line when its bitmap
identity, zoom, viewport size, or top-level render scaling changes, recording the
bitmap/control/viewport sizes, `TopLevel.RenderScaling`, the net
device-pixels-per-bitmap-pixel scale, and an explicit 1:1 verdict; bitmap swaps
identify their provenance (cached JPEG, fresh render, background refresh, resting
render). The gate is captured at process startup and off by default; when off, no
display observer is installed. Every line is appended to
`%LOCALAPPDATA%\Happy Photon\logs\display-trace.log`, truncated per process start —
the app is a WinExe, so the log file, not the console, is the reliable capture.
