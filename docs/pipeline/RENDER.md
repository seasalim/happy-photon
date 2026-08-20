# Render: `BaseImage` × `EditSettings` to Pixels

`RenderPipeline` is the single pixel path shared by preview, histogram, and export.
The formulas below explain how it preserves linear-light headroom while reducing the
tonal work to one quantization step. All Magick.NET processing remains Q16.

## 1. Stage order (fixed)

```
1 Geometry     rotate90 → horizon rotation (+ safe-crop intersect) → crop
2 Matrix       crossing on: AgX inset × WB; crossing off: WB (§4)
3 Tone LUT     source-kind tone regime, fused with matrix storage (§5)
4 Matrix       crossing on: AgX outset; crossing off: identity
5 Chroma       one combined saturation·vibrance Modulate (§6)
6 Detail       capture sharpen, chroma NR (§9)
7 Output       linear resize → output sharpen → target convert → encode (OUTPUT.md)
```

`RenderGeometry` owns the rotation and crop sequence, including
`CropGeometry.SafeBoundsAfterRotation`, `ResetPage`, and crop intersection.
Crop contract with horizon rotation: a null crop auto-applies the horizon safe
bounds, while an explicit full-image crop keeps the whole rotated canvas — the
crop tool previews with a full-image crop so the overlay's normalized
coordinates match the displayed bitmap. The remaining stages operate on that
geometry result in a fixed order.

### 1.1 Request contract

```csharp
public sealed record RenderOptions(
    bool ComputeStats = true,
    bool ComputeOverlayMasks = false,
    ClippingOverlaySide OverlaySides = ClippingOverlaySide.Both);
public sealed record RenderRequest(
    BaseImage Base, EditSettings Settings, RenderIntent Intent,
    int? MaxDimension, RenderOptions Options,
    OutputColorSpace OutputColorSpace = OutputColorSpace.Srgb);
```

- `OutputColorSpace` selects sRGB (default) or Display P3 only in finalization. Preview
  always forces sRGB. Geometry, tone, chroma, and detail are target-independent;
  `Intent`, `Options`, and `MaxDimension` otherwise change auxiliary work such as
  statistics, optional overlay masks, and the resize target.
- **Base immutability:** `RenderPipeline` never mutates `Base.Pixels`; it clones
  internally before stage 1. `BaseImage` lifetime is owned by the caller
  (`PreviewService` generation logic / export loop), never by the pipeline.
- **Resize domain:** every downscale — preview `MaxDimension` and export variants —
  runs in linear light with the same filter (Magick default Lanczos): sRGB-decode →
  `Resize` → sRGB-encode (the preview *base* is already linear and is resized before
  encoding). Note the honest limit of this rule: preview resizes the neutral base
  *before* tone mapping, export resizes the rendered result *after* it, and tone
  curves/clamps do not commute with resampling — the two paths are a deliberate,
  performance-driven **approximation of each other, not mathematically equivalent**.
  The shared filter + linear domain minimize the divergence; the WYSIWYG ΔE bounds
  (TESTING.md §3, row 3) are what actually govern it.

## 2. Why matrix → single LUT → matrix

- The chromatic part of WB and the AgX inset/outset are 3×3 matrices; none is
  expressible as per-channel curves.
- Everything between the matrices is a per-channel 1D function. `AgxCrossing` evaluates
  inset → exact 65,536-entry interpolated tone table → outset in `double`, then makes
  one Q16 write. Crossing-off degenerates to WB → retained display chain → identity.
  This avoids clipped intermediates and cumulative requantization while keeping slider
  ticks bounded to one fused pass plus optional chroma/detail work.

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

`ToneLut.Compose(ToneParams p) → double[65536]` is pure and unit-testable. Entry `i`
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

`EVsource` is `BaseImageInfo.SourceExposureBiasEv`. RAW loaders first solve a bounded
scalar EV against the file's normalized embedded preview. If that preview is missing
or unusable—or its estimate differs from a nonzero Fuji MakerNote bias by more than
0.5 EV—Fujifilm RAFs fall back to their MakerNote mid-point shift (or DR200/DR400
mode). Every other source falls back to 0. Standard images always use 0.

Each step is monotone non-decreasing on its domain and `clamp01` preserves (non-strict)
monotonicity, so the composed LUT is non-decreasing whenever the user curve is —
flat plateaus from clamping are expected and legal. Apply with
`ToneLutApplicator`, which linearly interpolates the exact 65,536 entries on the
unrounded matrix result and writes Q16 once. The former 4,096-entry table, rounded-index
lookup, and Magick `Clut` parity contract are retired.
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

Saturation and vibrance are both HSL saturation scalings and therefore compose
multiplicatively into **one** Modulate call. Combining them avoids an intermediate
clamp and rounding step; for example, saturation +100 followed by vibrance −100
resolves cleanly to identity rather than clipping saturated colors first:

```
satFactor = (100 + Saturation)/100 · (100 + Vibrance·0.5)/100
Modulate(100, satFactor·100, 100)        // skip when satFactor == 1
```

Runs after the crossing, on sRGB-encoded display Rec.2020, where Q16 clamping is benign.

## 7. Histogram & clipping

Computed at preview scale when `Options.ComputeStats` (existing `HistogramService`
bins stay 8-bit). Shadow statistics always sample the finalized display. Highlight
statistics depend on the tone regime:

The same render-stats call makes a second pass over the histogram's exact
downsampled Q16 RGB buffer to accumulate a 256-column × 128-level luminance
waveform. Horizontal image position maps to columns, Rec.601 luminance maps with
`level = value8 >> 1`, and each `ushort` cell stores the sample count. Sources
narrower than 256 pixels back-fill unrepresented columns. At the 1024 px render-stats
cap, no cell can exceed 4096 samples. Library thumbnail histograms use the bitmap
overload and never create waveform data.

The selectable RAW histogram is deliberately outside that render stage. `RawBaseLoader`
captures it from LibRaw's preserved post-`Unpack` mosaic before output configuration,
white balance, demosaic, camera conversion, highlight reconstruction, and tone. It walks
only the visible window at `top_margin + row`, `left_margin + column`, using
`raw_pitch / sizeof(ushort)` as the full-frame stride. CFA phase and repeating black
blocks use visible coordinates. Bayer tables (`filters > 1000`) and the 6×6 X-Trans
table (`filters == 9`) are supported; both green phases merge into green.

For photosite value `v` and native channel `ch`, RAW binning uses
`black_ch = black + cblack[ch] + repeatingBlock`,
`n = clamp01((v - black_ch) / max(1, maximum - black_ch))`, then
`round(E(n) * 255)` with §3's sRGB encode. A bounded lookup performs the encode; the
visible pass contains no `Math.Pow` and never strides over photosites. RAW clipping is
the separate linear test `v >= maximum`, counted per sensor channel. It is not inferred
from bin 255 and is distinct from `ClippingStats.RawNearClip`, which describes already
demosaiced display-basis base pixels.

```csharp
public sealed record ChannelClip(double R, double G, double B);   // fractions 0..1
public sealed record ClippingStats(
    ChannelClip High,        // RAW: scene channel ≥ 1 after WB+gain, before inset;
                             // standard: display channel ≥ 254.5/255
    ChannelClip Low,         // per channel: fraction ≤ 0.5/255
    double HighAny,          // same regime-specific high threshold, any channel
    double LowAll,           // all-channels-low fraction (drives the blue overlay/chip)
    double RawNearClip);     // raw bases only, else 0: fraction of base pixels with any
                             // display-basis channel ≥ 0.99 after a linear Rec.2020→sRGB
                             // conversion but BEFORE the render matrix/LUT. This is "at or near
                             // sensor clip *as decoded*" — LibRaw's highlight
                             // reconstruction has already run, so it is an indicator
                             // of unrecoverable areas, not a sensor-domain measurement.
```

For RAW, `High`/`HighAny` are exposure- and WB-sensitive scene facts measured after
geometry and before the inset. `RawNearClip` remains the edit-independent legacy
decoded-near-clip fact above; sensor mosaic clip counts remain authoritative for true
sensor clip. When `Options.ComputeOverlayMasks` is true, masks follow the requested
semantic sides: scene highlights and/or display floor. Standard-source requests always
suppress the scene-highlight side. Develop requests masks only while the `J` latch or a
triangle peek is active; ordinary preview renders remain mask-free.

## 8. EditSettings v2 — schema and storage

JSON document shape (canonical field order for hashing):

```jsonc
{
  "version": 2,
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
              "noiseReduction": "off",   // off | light | full  (FBDD, decode-affecting)
              "chromaNr": 0 },           // 0-100
  "rotation": 0, "horizon_rotation": 0.0, "crop": null,
  "curve": { },
  "curveRed": { },                       // optional; omitted = identity
  "curveGreen": { },                     // optional; omitted = identity
  "curveBlue": { },                      // optional; omitted = identity
  "applied_preset_id": null
}
```

The three channel fields follow `curve` in the shown order and use null-omission
semantics: `null` is never serialized. Legacy v2 documents therefore remain
byte-identical after normalization, and `Clamp` validates/rebuilds an optional curve
only when the field was present. Selecting a channel in the UI does not materialize it.

`hlReconstruction` and `detail.noiseReduction` are the **decode-affecting subset**;
they project into `BaseDecodeSettings` (OVERVIEW.md §4, DECODE.md §4) and changing
them re-decodes the base rather than re-rendering it.

The Develop Detail group exposes all three fields. Capture sharpening resolves a
`null` value to RAW 25 or standard 0 and canonicalizes the matching default back to
`null`; FBDD remains visible but disabled for standard sources. Preview detail uses
the bounded preview base, while export-scale renders are the fidelity reference.

### 8.1 Catalog storage (the actual schema, [CatalogSchema.cs](../../Services/CatalogSchema.cs))

The canonical `images` table contains `id`, `file_path`, `file_name`, `edit_settings`,
`edit_version`, `flag_state`, `rating`, and `updated_utc`. New rows always receive a
complete v2 JSON document and `edit_version = 2`.

`CatalogSchema` creates that table for a new catalog and validates the required column
names on startup. It does not add columns or migrate older layouts. Extra columns are
tolerated but ignored; a table missing any required column fails during startup with an
actionable instruction to move the entire catalog folder aside before Retry. Keeping the
folder intact prevents recycled catalog IDs from resolving to thumbnails or previews
belonging to the incompatible database.

The read path is row-local and never writes:

- marker 2 + valid document → parse and return;
- out-of-range current values → clamp in memory and log once;
- null or malformed document, or any marker other than 2 → log once and return neutral
  current settings.

One corrupt row therefore cannot fail the folder's batched load. Single and batch edit
writes serialize the complete current document and marker; batch writes retain their
single-transaction all-or-nothing behavior.

### 8.2 Current-format boundaries

`EditSettingsJson.Serialize` requires `EditSettings.Version == 2`, clones the model,
clamps and validates the clone, then writes canonical JSON. It never changes the caller's
model and rejects every other version.

Preset files must explicitly declare both the current wrapper version and the current
settings version. Versionless or unsupported files are skipped; they are never rewritten
or upgraded while loading. Copy/paste accepts only current in-memory settings and rejects
a non-current source or target before applying values.

The MCP `apply_edit_settings` input defaults an omitted `version` to 2 and accepts only
version 2. Its white balance shape is the same `asShot`/`custom`/`preset`/`picked` model
shown above; there is no scalar temperature field or generic raw-gain mode. Unsupported
versions and modes are rejected before any image is mutated.
Because `apply_edit_settings` replaces tonal state without exposing channel curves,
it clears all three optional channel curves rather than retaining stale values.

## 9. Detail stage

The first-release UI does not expose these settings. Capture sharpening uses its
source-kind default, FBDD remains Off, and chroma NR remains 0. Their implementations
remain part of the shared pipeline and are covered by parity and performance tests.

All spatial parameters are **defined at native (full-base) resolution** and scale with
the render: `σ_effective = σ_native · renderLongEdge / max(Info.FullWidth, Info.FullHeight)`
(the native dimensions live on `BaseImageInfo`, set for preview bases too); skip the op
when `σ_effective < 0.3` px (perceptually nil). This is what keeps preview and export
consistent: at a 1600px preview of a 24MP image, capture sharpening is a deliberate
near-no-op — exactly how its full-res effect survives downscaling. Sharpening is
judged at export size (as in Lightroom), and the WYSIWYG goldens compare at preview
scale where both paths agree by construction.

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
  The implementation uses one parallel kernel over bands with at most 8 million
  core pixels. Each band carries the original lower horizontal halo and the vertical
  rolling sums, and reads an upper `r+1` halo before writing in place. The preceding
  tonal stage's full-image write has already detached the working clone's pixel cache.
  Band partitioning must be bit-identical to a single band; there is no separate
  streaming formula.
- **FBDD** (raw, decode-time): `BaseDecodeSettings.NoiseReduction` → wrapper's
  `fbdd_noiserd` 0/1/2 — lives in `RawBaseLoader`; changing it invalidates the base
  (DECODE.md §4), not merely the render.
- **Output sharpen**: OUTPUT.md §3.

## 10. Performance contract

Preview rendering always calculates the histogram and luminance waveform from the same
display-referred sRGB buffer before bitmap conversion. The waveform pass is synchronous
inside `HistogramService` and inherits the render's surrounding cancellation checks. For an edited
RAW whose generation is still current, `PreviewService` converts the full preview, then
transfers exclusive ownership of `RenderResult.Image` to a tracked background task. The
task resizes that image in place to the explicit Library request's generation dimension,
capped at 512px, with `RenderColorEncoding.ResizeInLinearLight`, then converts the
derived thumbnail. No full-size clone is made. This is a cache artifact, not a new render
stage, so it does not change `RenderPipeline.Version`. Generation checks run before the
ownership transfer; superseded generations skip the work, and non-RAW or unedited
renders do not create a candidate. Promotion is a bounded bitmap clone and never waits
on background work. Shutdown awaits candidate creation and cache queueing before the
rendered-thumbnail writer is drained.

Per slider tick at 1600px preview: geometry (usually no-op) + the fused matrix → tone
LUT → matrix pass, then optional combined Modulate/detail work. The development budget
is ≤ 150 ms and is measured with `HAPPY_PHOTON_PERF=1`; the exact tone tables are cached
for the bounded active settings set. Tonal, chroma, and
geometry slider moves invalidate only the render; only `BaseDecodeSettings` changes
invalidate the base.

`HAPPY_PHOTON_DISPLAY_TRACE=1` enables the permanent display-chain diagnostic. The
active Develop or fullscreen preview emits one post-layout line when its bitmap identity,
zoom, viewport size, or top-level render scaling changes. The line records bitmap pixels,
image-control and viewport logical sizes, `TopLevel.RenderScaling`, the device rectangle,
net device-pixels-per-bitmap-pixel scale, and an explicit 1:1 verdict. Preview bitmap
swaps separately identify cached JPEG, fresh render, or background refresh provenance.
The gate is captured at process startup, is independent of `HAPPY_PHOTON_PERF`, and is
off by default; when off, the view installs no display observer and log interpolation
does not evaluate bitmap dimensions. Besides console/debug output, every trace line is
appended to `%LOCALAPPDATA%\Happy Photon\logs\display-trace.log`, truncated at each
process start so it holds the last session. The app is a WinExe, so `dotnet run` shows
no console output — the log file is the reliable capture.
