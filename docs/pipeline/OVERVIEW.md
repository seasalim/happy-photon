# Happy Photon Image Pipeline

Happy Photon uses one decode model and one render model for every supported source.
This document explains that architecture and the invariants behind it. The sibling
documents cover the individual stages in greater depth.

| Doc | Covers |
|-----|--------|
| [DECODE.md](DECODE.md) | Sources → `BaseImage` (loaders, LibRaw params, ICC normalize, caching) |
| [RENDER.md](RENDER.md) | `BaseImage` + `EditSettings` → rendered image (stage order, LUT math) |
| [WHITE_BALANCE.md](WHITE_BALANCE.md) | WB model: CCT/tint math, presets, eyedropper, matrices |
| [OUTPUT.md](OUTPUT.md) | Export encoding, ICC tagging, metadata policy, variants |
| [TESTING.md](TESTING.md) | Golden harness, sample assets, tolerances, determinism |

## 1. The two-step model

Everything reduces to two pure-ish functions:

```
Load:   source file × BaseDecodeSettings ─────▶ BaseImage        (decode-dependent only)
Render: BaseImage × EditSettings × RenderIntent ▶ pixels + stats  (edit-dependent)
```

- **`BaseDecodeSettings`** is the small, decode-affecting projection of `EditSettings`
  (today: highlight reconstruction, FBDD noise reduction — both raw-only). Everything
  else in `EditSettings` affects only the render. Changing a decode-affecting field
  re-decodes the base in the background (DECODE.md §4); changing anything else never
  does. The in-memory base is keyed by (file, decode settings, size class).
- **`BaseImage`** is the decoded, normalized, canonical representation of a source file
  under given decode settings: 16-bit unsigned, **linear light, sRGB primaries, D65
  white**, orientation applied, no look of any kind baked in. RAW reaches it through
  LibRaw; JPEG/PNG/TIFF/HEIC reach it through Magick decode + embedded-ICC transform +
  linearization. See DECODE.md.
- **`RenderPipeline`** is the only code path that turns a base + settings into visible
  pixels. Preview, histogram, clipping stats, and export all call it. There is no second
  pipeline, no preview-only shortcut that changes pixels, no export-only fixup.

## 2. Invariants

1. **WYSIWYG:** export pixels == preview pixels for the same settings, up to resolution
   and resize resampling (golden-tested, TESTING.md §4).
2. **Determinism:** same base + same settings → identical output, independent of image
   content history, decode size, platform defaults, or time. No auto-anything inside the
   pipeline (auto modes are UI actions that *write settings*, never render-time behavior).
3. **No baked look:** `BaseImage` is linear and neutral. Aesthetic decisions live in
   explicit render settings or source-kind defaults rather than the decoded pixels.
4. **No clipped intermediates:** linear-domain gains never materialize values that a Q16
   buffer would clamp. Chromatic 3×3 matrices are pre-normalized (gain refunded inside the
   tone LUT); all per-channel gain/roll-off happens analytically inside one composed LUT.
   (This is why the plain `Magick.NET-Q16` package suffices — do not switch to HDRI.)
5. **Source-agnostic render:** `RenderPipeline` never branches on file type. Capability
   differences (highlight reconstruction, FBDD, capture-sharpen default) are expressed in
   `BaseImageInfo` and settings defaults, decided at load/UI time.
6. **Originals are never modified.**
7. Pipeline services remain independent of UI state. ViewModels translate controls into
   settings and marshal completed pixels; views only display them.
8. **Bases are immutable, decodes are single-flight.** `RenderPipeline` never mutates
   `BaseImage.Pixels` (it clones internally); base lifetime belongs to the caller.
   Decodes coalesce newest-wins per (file, decode settings, size class); tonal/chroma/
   geometry setting changes never trigger a decode (DECODE.md §4).

## 3. Stage diagram

```
            ┌─ DECODE.md ─────────────────────────────────────────────┐
 RAW ──LibRaw(16-bit, linear, no-auto-bright, camWB, chosen highlights)┤
 JPEG/PNG/TIFF/HEIC ──Magick decode ─ICC→sRGB─ linearize ─────────────┤
            └──────────────► BaseImage (linear sRGB16 + BaseImageInfo)┘
                                   │
            ┌─ RENDER.md ──────────▼──────────────────────────────────┐
            │ 1 Geometry   rotate90 → horizon(+safe crop) → crop      │
            │ 2 Chromatic  WB 3×3 matrix (normalized)   [WHITE_BALANCE]│
            │ 3 Tone LUT   exposure→shoulder→sRGB-encode→look→B/C→    │
            │              shadows/highlights→user curve  (ONE Clut)  │
            │ 4 Chroma     saturation, vibrance (Modulate)            │
            │ 5 Detail     capture sharpen, chroma NR                 │
            └──────────────┬───────────────────────────────┬──────────┘
                     histogram + clipping stats            │
            ┌─ OUTPUT.md ──▼───────────────────────────────▼──────────┐
            │ display: ConvertToBitmap (8-bit BGRA)                   │
            │ export: resize → output sharpen → encode + sRGB ICC     │
            │         + metadata policy (EXIF copy, GPS toggle)       │
            └─────────────────────────────────────────────────────────┘
```

## 4. Runtime contracts

```csharp
public enum BaseSourceKind { RawLibRaw, Standard, HeicPlatform }

public enum HlReconstructionMode { Blend, Clip }
public enum FbddMode { Off, Light, Full }

public sealed record BaseDecodeSettings(HlReconstructionMode HlReconstruction, FbddMode NoiseReduction)
{
    public static BaseDecodeSettings Default { get; }        // Clip + Off
    public static BaseDecodeSettings From(EditSettings s);   // projects both decode-affecting fields
    public string CacheKey { get; }                          // base-v{BaseImage.Version};hl=…;fbdd=…
}

public sealed record BaseImageInfo(
    BaseSourceKind Kind,
    bool IsRawSource,              // true only for mosaic sources decoded via LibRaw
    BaseDecodeSettings Decode,     // the settings this base was decoded with
    double[]? CamMul,              // length 3, or native LibRaw length 4; null if unavailable
    double[,]? CamToSrgb,          // camera → linear sRGB, 3×CamMul.Length; null if unavailable
    double AsShotKelvin,           // raw estimate or 5500 fallback; 6504 non-raw
    double AsShotTint,             // raw estimate or 0 fallback; 0 non-raw
    bool HadIccProfile,
    string? IccDescription,
    int ExifOrientationApplied,    // for diagnostics; pixels are already upright
    int FullWidth,                 // native full-resolution dimensions after orientation —
    int FullHeight,                // set on preview bases too; RENDER.md §9 scales σ by these
    double SourceExposureBiasEv = 0); // Fuji midpoint restoration; 0 for other sources

public sealed class BaseImage : IDisposable
{
    public const int Version = 2;        // bump whenever decoded pixels or facts change
    public const int PreviewMaxDimension = 1600;
    public MagickImage Pixels { get; }   // Depth 16, ColorSpace RGB (linear), no profiles
    public BaseImageInfo Info { get; }
}

public enum RenderIntent { Preview, Export }

public sealed record RenderOptions(bool ComputeStats = true, bool ComputeOverlayMasks = false);

public sealed record RenderRequest(
    BaseImage Base, EditSettings Settings, RenderIntent Intent,
    int? MaxDimension, RenderOptions Options);   // semantics: RENDER.md §1.1

public sealed class RenderResult : IDisposable
{
    public MagickImage Image { get; }        // display-referred sRGB, 16-bit
    public ClippingStats Clipping { get; }   // see RENDER.md §7
    public MagickImage? OverlayMask { get; } // only when Options.ComputeOverlayMasks
}
```

`EditSettings` v2 schema and current storage contract: RENDER.md §8.

The first-release UI keeps spatial detail at fixed defaults: RAW capture sharpening 25,
other capture sharpening 0, FBDD Off, and chroma NR 0. Presets, copy/paste, and MCP do
not expose those fields; only export output sharpening is user-adjustable.

`BaseImage` exclusively owns `Pixels` after construction. Callers may hold a base across
multiple renders but must dispose it only after those renders finish; disposal is
idempotent and accessing `Pixels` afterward throws. A loader returning `null` retains
ownership of any temporary image it created. `BaseImageInfo` is loader-produced factual
metadata and consumers treat it as immutable.

## 5. Service map

| File | Role |
|------|------|
| `Services/BaseImage.cs` | `BaseImage`, `BaseImageInfo`, `BaseSourceKind`, `BaseDecodeSettings` |
| `Services/IBaseImageLoader.cs` + `BaseLoaderRouter.cs` | route by format |
| `Services/RawBaseLoader.cs` | LibRaw decode → base (DECODE.md §2) |
| `Services/StandardBaseLoader.cs` | Magick decode + ICC normalize → base (DECODE.md §3) |
| `Services/RenderPipeline.cs` | stage orchestration and result ownership (RENDER.md) |
| `Services/RenderGeometry.cs` | rotation, horizon correction, and crop |
| `Services/ToneLut.cs` | pure LUT composition (RENDER.md §5) |
| `Services/ToneLutApplicator.cs` | Q16 LUT interpolation, exhaustively pinned to Magick Clut |
| `Services/RenderChromaticStage.cs` | white-balance matrix application |
| `Services/RenderDetail.cs` + `RenderSharpening.cs` | fixed detail operations |
| `Services/WhiteBalanceModel.cs` | CCT/tint ↔ gains math (WHITE_BALANCE.md) |
| `Services/ChromaticAdaptation.cs` | Bradford matrices, normalization |
| `Services/ClippingStats.cs` | clip counters + overlay masks |
| `Services/ExportMetadataService.cs` | EXIF copy/strip policy (OUTPUT.md §4) |

`IRawProcessingService` is intentionally outside this base/render path. It remains the
thumbnail-extraction and metadata fallback used by the browsing pipeline.

## 6. Pipeline versioning

`RenderPipeline.Version` participates in the settings hash used by the rendered-preview
cache (DECODE.md §5) and selects the matching golden baseline. A visible render-math
change increments it, which invalidates rendered caches and makes the corresponding
golden update explicit. Decode changes increment `BaseImage.Version` similarly.
`BaseDecodeSettings.CacheKey` is the invariant, culture-independent string
`base-v{BaseImage.Version};hl={blend|clip};fbdd={off|light|full}`. In-memory identity
adds normalized file path and preview/full size class; rendered-cache settings hashes
still include `BaseImage.Version` separately as specified in DECODE.md §5.

## 7. Current boundaries

Local adjustments/masks, lens & perspective corrections, custom output ICC targets,
display-profile awareness, XMP sidecars, HDR output, AVIF/JXL, 1:1-zoom region decode
(zoom continues to use the bounded preview base). These are product boundaries, not
partially implemented pipeline stages.
