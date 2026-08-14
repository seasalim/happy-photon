# Render: `BaseImage` × `EditSettings` to Pixels

`RenderPipeline` is the single pixel path shared by preview, histogram, and export.
The formulas below explain how it preserves linear-light headroom while reducing the
tonal work to one quantization step. All Magick.NET processing remains Q16.

## 1. Stage order (fixed)

```
1 Geometry     rotate90 → horizon rotation (+ safe-crop intersect) → crop
2 Chromatic    3×3 WB matrix via ColorMatrix (pre-normalized, §4)
3 Tone LUT     one composed 1D LUT via Clut (§5)
4 Chroma       one combined saturation·vibrance Modulate (§6)
5 Detail       capture sharpen, chroma NR (§9)
6 Output       display convert / resize + output sharpen + encode (OUTPUT.md)
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
public sealed record RenderOptions(bool ComputeStats = true, bool ComputeOverlayMasks = false);
public sealed record RenderRequest(
    BaseImage Base, EditSettings Settings, RenderIntent Intent,
    int? MaxDimension, RenderOptions Options);
```

- `Intent`, `Options`, and `MaxDimension` change **auxiliary work** such as statistics,
  optional overlay masks, and the resize target. They do not change per-pixel math in
  stages 1–5. Preview and export differ only in base resolution and resize target.
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

## 2. Why matrix + single LUT

- The chromatic part of WB is a 3×3 matrix — not expressible as per-channel curves.
- Everything tonal (exposure gain, highlight shoulder, sRGB encode, base look,
  brightness/contrast, shadows/highlights, user curve) is a **1D function**, identical
  for R, G, B. Composing them into one LUT applied once means: no clipped
  intermediates (invariant 4), no cumulative requantization (one rounding, not seven),
  and slider ticks cost at most one `ColorMatrix` + one `Clut` + one `Modulate`.

## 3. Notation

- `E(x)`: sRGB encode. `E(x) = 12.92x` for `x ≤ 0.0031308`, else `1.055·x^(1/2.4) − 0.055`.
- `D(y)`: inverse (decode). `y/12.92` for `y ≤ 0.04045`, else `((y+0.055)/1.055)^2.4`.
- `clamp01(x) = min(max(x, 0), 1)`. Slider ranges are the existing UI ranges.

## 4. Chromatic stage

`WhiteBalanceModel` yields a raw 3×3 matrix `M` in linear sRGB (WHITE_BALANCE.md §4).
Before use:

```
normScale = max over rows i of Σ_j max(M[i,j], 0)     // ≥ 1 ⇒ some input could exceed 1
Mn        = M / normScale                              // outputs of [0,1]³ stay ≤ 1
fold      = normScale                                  // refunded inside the tone LUT
```

Apply `Mn` with `MagickImage.ColorMatrix`. Negative coefficients may drive rare
out-of-gamut pixels to 0 (clamped) — accepted, standard behavior. `WbMode.AsShot` with
no other change must produce `Mn = I, fold = 1` **exactly** (skip the ColorMatrix call).

## 5. Tone LUT

`ToneLut.Compose(ToneParams p) → ushort[4096]` is pure and unit-testable. Entry `i`
uses `v = i/4095`, a linear post-matrix value. Display-domain operators are defined
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
u  = curve(t)                            // §5.5  input already ∈ [0,1]
lut[i] = round(clamp01(u) · 65535)
```

`EVsource` is `BaseImageInfo.SourceExposureBiasEv`. RAW loaders first solve a bounded
scalar EV against the file's normalized embedded preview. If that preview is missing
or unusable—or its estimate differs from a nonzero Fuji MakerNote bias by more than
0.5 EV—Fujifilm RAFs fall back to their MakerNote mid-point shift (or DR200/DR400
mode). Every other source falls back to 0. Standard images always use 0.

Each step is monotone non-decreasing on its domain and `clamp01` preserves (non-strict)
monotonicity, so the composed LUT is non-decreasing whenever the user curve is —
flat plateaus from clamping are expected and legal. Apply with
`ToneLutApplicator`, which linearly interpolates the 4096 entries directly into the
Q16 RGB pixel cache. Its exhaustive 65,536-input tests pin it bit-for-bit to
`image.Clut(lutImage, PixelInterpolateMethod.Bilinear, Channels.RGB)`; the direct
implementation avoids the latter's slider-budget regression without changing pixels.
The identity settings vector must produce, for non-raw bases,
`lut[i] ≈ E(i/4095)` — a JPEG with zero edits renders back to its original appearance
within 1 LSB at 8 bits (regression test).

### 5.1 Highlight shoulder (Highlights slider H ∈ [−100, 0])

```
k = 1 + H/100 · 0.55                     // knee ∈ [0.45, 1]
shoulder(x, k) = x                        for x ≤ k
               = k + (1−k)·tanh((x−k)/(1−k))   for x > k   (when k < 1)
               = min(x, 1)                     (when k = 1)
```

C1-continuous at the knee, strictly monotone, asymptote 1.0. At `H = 0` this is
identity-then-clip, so unedited images are unaffected.

### 5.2 Shadows (S ∈ [−100, 100], display domain, input ∈ [0,1])

`x + S/100 · 0.35 · x(1−x)³` — zero at both ends, peak effect near x ≈ 0.25.
On [0,1] it is monotone for the full slider range (|d/dx x(1−x)³| ≤ 1) and its output
stays inside [0,1] (monotone with fixed endpoints 0 and 1). These properties do **not**
hold outside [0,1] — hence the clamp before this step.

### 5.3 Highlights, positive side (H ∈ (0, 100], input ∈ [0,1])

`x + H/100 · 0.30 · x³` — monotone on [0,1]; output can reach 1.3, hence the clamp
after. The negative side is §5.1's knee.

### 5.4 Base look (default on for raw bases, off for others)

Exact port of the current decode-time curve, now optional and float:

```
baseLook(x) = x + 0.012(1−x)³ − 0.10·sin(2πx)·4x(1−x) − 0.03x³
```

Monotone on [0,1] (derivative ≥ 1 − 0.63 > 0) and range ⊂ [0,1].
`EditSettings.BaseLook == null` means "default by source kind." RAW bases default on
and standard bases default off. The first-release Develop panel does not expose this
internal setting.

### 5.5 User curve

Existing `CurveData` 256-entry table, evaluated with linear interpolation between
entries at LUT-composition time (input `t·255`). `CurveData` orders points by X but
does **not** constrain Y ([CurveData.cs](../../Models/CurveData.cs)) — a user can
draw a decreasing curve, and that is allowed (deliberate solarization is user intent,
not a pipeline bug). Consequently the global monotonicity *property test* runs with
identity/monotone curves only (TESTING.md §4.1); everything upstream of the curve must
stay monotone unconditionally.

## 6. Chroma stage

Saturation and vibrance are both HSL saturation scalings and therefore compose
multiplicatively into **one** Modulate call. Combining them avoids an intermediate
clamp and rounding step; for example, saturation +100 followed by vibrance −100
resolves cleanly to identity rather than clipping saturated colors first:

```
satFactor = (100 + Saturation)/100 · (100 + Vibrance·0.5)/100
Modulate(100, satFactor·100, 100)        // skip when satFactor == 1
```

Runs after the LUT, display-referred, where Q16 clamping is benign.

## 7. Histogram & clipping

Computed from the stage-4/5 output at preview scale when `Options.ComputeStats`
(existing `HistogramService` bins stay 8-bit).

```csharp
public sealed record ChannelClip(double R, double G, double B);   // fractions 0..1
public sealed record ClippingStats(
    ChannelClip High,        // per channel: fraction ≥ 254.5/255 (display domain)
    ChannelClip Low,         // per channel: fraction ≤ 0.5/255
    double HighAny,          // any-channel-high fraction (drives the red overlay/chip)
    double LowAll,           // all-channels-low fraction (drives the blue overlay/chip)
    double RawNearClip);     // raw bases only, else 0: fraction of base pixels with any
                             // channel ≥ 0.99 BEFORE matrix/LUT. This is "at or near
                             // sensor clip *as decoded*" — LibRaw's highlight
                             // reconstruction has already run, so it is an indicator
                             // of unrecoverable areas, not a sensor-domain measurement.
```

When `Options.ComputeOverlayMasks` is true, a mask bitmap at render resolution is produced from
the same thresholds (highlight = any channel high → red tint; shadow = all channels
low → blue tint) and returned on `RenderResult`. The current preview and export callers
leave this option off; masks remain an internal render capability and never touch
exported pixels.

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
  "highlights": 0, "shadows": 0,         // §5.1–5.3
  "brightness": 0, "contrast": 0,        // §5
  "saturation": 0, "vibrance": 0,
  "baseLook": null,                      // null = source-kind default
  "hlReconstruction": "clip",            // raw only: blend | clip  (decode-affecting)
  "detail": { "captureSharpen": null,    // null = default (raw 25, else 0); 0-100
              "noiseReduction": "off",   // off | light | full  (FBDD, decode-affecting)
              "chromaNr": 0 },           // 0-100
  "rotation": 0, "horizon_rotation": 0.0, "crop": null,
  "curve": { }, "applied_preset_id": null
}
```

`hlReconstruction` and `detail.noiseReduction` are the **decode-affecting subset**;
they project into `BaseDecodeSettings` (OVERVIEW.md §4, DECODE.md §4) and changing
them re-decodes the base rather than re-rendering it.

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
- **Chroma NR** (0–100): preserve BT.709-weighted luma
  `Y = 0.2126R + 0.7152G + 0.0722B`; blur `Cb = B−Y` and `Cr = R−Y` with an
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

Preview rendering calculates the histogram before display conversion. For an edited
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

Per slider tick at 1600px preview: geometry (usually no-op) + at most three
full-image passes — ColorMatrix (skipped at as-shot), Clut, combined Modulate (skipped
at neutral). The development baseline budget is ≤ 150 ms and is measured with
`HAPPY_PHOTON_PERF=1`. LUT composition itself is microseconds. Tonal, chroma, and
geometry slider moves invalidate only the render; only `BaseDecodeSettings` changes
invalidate the base.
