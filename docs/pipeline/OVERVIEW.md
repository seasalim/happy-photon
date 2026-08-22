# Happy Photon Image Pipeline

Happy Photon uses one decode model and one render model for every supported source.
This document explains that architecture and the invariants behind it. The sibling
documents cover the individual stages in greater depth.

| Doc | Covers |
|-----|--------|
| [WORKING_SPACE.md](WORKING_SPACE.md) | Canonical Rec.2020 basis, matrices, ICC target, provenance |
| [CHARACTERIZATION.md](CHARACTERIZATION.md) | Camera-RGB contract, built-in characterization, DCP math |
| [DECODE.md](DECODE.md) | Sources → `BaseImage` (loaders, LibRaw params, ICC normalize, caching) |
| [OPTICS.md](OPTICS.md) | Embedded DNG/Fuji prescriptions and fused sampling |
| [RENDER.md](RENDER.md) | `BaseImage` + `EditSettings` → rendered image (stage order, LUT math) |
| [TONE_ENGINE.md](TONE_ENGINE.md) | AgX crossing and tone engine: formula authority, clean-room provenance |
| [WHITE_BALANCE.md](WHITE_BALANCE.md) | WB model: CCT/tint math, presets, eyedropper, matrices |
| [OUTPUT.md](OUTPUT.md) | Export encoding, ICC tagging, metadata policy, variants |
| [UI.md](UI.md) | Pipeline UI surface: controls, gating, interactions |
| [TESTING.md](TESTING.md) | Golden harness, sample assets, tolerances, determinism |

## 1. The two-step model

Everything reduces to two pure-ish functions:

```
Load:   source file × BaseDecodeSettings ─────▶ BaseImage        (decode-dependent only)
Render: BaseImage × EditSettings × RenderIntent ▶ pixels + stats  (edit-dependent)
```

- **`BaseDecodeSettings`** is the small, decode-affecting projection of `EditSettings`
  (highlight reconstruction, FBDD noise reduction, optics toggles, and camera-profile selection — all
  raw-only). Everything
  else in `EditSettings` affects only the render. Changing a decode-affecting field
  re-decodes the base in the background (DECODE.md §4); changing anything else never
  does. The in-memory base is keyed by (file, decode settings, size class).
- **`BaseImage`** is the decoded, normalized, canonical representation of a source file
  under given decode settings: 16-bit unsigned, **linear light, Rec.2020 primaries, D65
  white**, orientation applied, no look of any kind baked in. RAW reaches it through
  LibRaw; JPEG/PNG/TIFF/HEIC reach it through Magick decode and either a profiled ICC
  transform or the equivalent sRGB EOTF plus matrix. See DECODE.md and WORKING_SPACE.md.
  A true monochrome RAW is still represented as RGB, with its one gray sensor plane
  replicated to exact equal channels and identified by `BaseImageInfo.IsMonochrome`.
- **`RenderPipeline`** is the only code path that turns a base + settings into visible
  pixels. Preview, histogram, clipping stats, and export all call it. There is no second
  pipeline, no preview-only shortcut that changes pixels, no export-only fixup.

## 2. Invariants

1. **WYSIWYG:** preview and export agreement is judged colorimetrically for the same
   settings, up to the tested target/decode/resize bounds (TESTING.md §3). Preview and
   default sRGB export also agree in raw codes; Display P3 uses different codes and is
   compared through its embedded profile in a common space. All shared edit stages precede
   the target fork; only convert, clamp, and encode are target-dependent.
2. **Determinism:** same base + same settings → identical output, independent of image
   content history, decode size, platform defaults, or time. No auto-anything inside the
   pipeline (auto modes are UI actions that *write settings*, never render-time behavior).
3. **No baked look:** `BaseImage` is linear and neutral. Aesthetic decisions live in
   explicit render settings or source-kind defaults rather than the decoded pixels.
4. **No clipped intermediates:** linear-domain gains never materialize values that a Q16
   buffer would clamp. Chromatic 3×3 matrices are pre-normalized (the fold is refunded
   inside the active tone regime); all per-channel gain/roll-off happens analytically
   inside one exact 65,536-entry LUT.
   (This is why the plain `Magick.NET-Q16` package suffices — do not switch to HDRI.)
5. **Source-kind tone regime:** `BaseImageInfo.IsRawSource` selects the scene-referred
   AgX crossing for RAW and the identity-preserving display-referred chain for standard
   sources. There is no exposure trigger or persisted crossing toggle.
6. **Originals are never modified.**
7. Pipeline services remain independent of UI state. ViewModels translate controls into
   settings and marshal completed pixels; views only display them.
8. **Bases are immutable, decodes are single-flight.** `RenderPipeline` never mutates
   `BaseImage.Pixels` (it clones internally); base lifetime belongs to the caller.
   Decodes coalesce newest-wins per (file, decode settings, size class); tonal/chroma/
   geometry setting changes never trigger a decode (DECODE.md §4). Preview loading
   returns an interactive/large pair from one bounded decode; the active fitted or
   manually zoomed view selects a resting render target and is never part of base
   identity.
9. **Background work does not hydrate cloud sources.** A live source-availability gate
   wraps base loaders and guards metadata, thumbnails, and path-based statistics.
   Cached output may be displayed without source content. Only a single-image
   **Download and open** action or a confirmed export batch may use approved hydration
   intent; agents always remain background intent.

## 3. Stage diagram

```
            ┌─ DECODE.md ─────────────────────────────────────────────┐
 RAW ──LibRaw(camera RGB)──optics/sample──characterize→Rec.2020 ─────┤
 mono RAW ──LibRaw(gray, linear)──bounded gray resize──replicate RGB ┤
 JPEG/PNG/TIFF/HEIC ──Magick decode ─color→linear Rec.2020 ──────────┤
            └──────────► BaseImage (linear Rec.2020 Q16 + BaseImageInfo)┘
                                   │
            ┌─ RENDER.md ──────────▼──────────────────────────────────┐
            │ 1 Geometry   rotate90 → horizon(+safe crop) → crop      │
            │ 2 DCP HueSat optional scene-linear ProPhoto profile map │
            │              (RENDER.md §2.1)                           │
            │ 3 Matrix     RAW: AgX inset × WB; standard: WB           │
            │ 4 Tone LUT   RAW: gain→log2→sigmoid→channel→master;    │
            │              standard: retained chain→channel→master   │
            │              retained display-domain chain (exact Q16)  │
            │ 5 Matrix     RAW: AgX outset; standard: identity         │
            │ 6 Chroma     OKLCh saturation + protected vibrance      │
            │ 7 Detail     capture sharpen, chroma NR (Rec.2020 luma) │
            └──────────────┬───────────────────────────────┬──────────┘
                     histogram + clipping stats            │
            ┌─ OUTPUT.md ──▼───────────────────────────────▼──────────┐
            │ display: ConvertToBitmap (8-bit BGRA)                   │
            │ shared: decode→linear resize→encode→output sharpen      │
            │         → vignette→grain                                │
            │ target: decode→sRGB/P3 convert→encode + matching ICC    │
            │         + metadata policy (EXIF copy, GPS toggle)       │
            └─────────────────────────────────────────────────────────┘
```

## 4. Runtime contracts

```csharp
public enum BaseSourceKind { RawLibRaw, Standard, HeicPlatform }

public enum HlReconstructionMode { Blend, Clip }
public enum FbddMode { Off, Light, Full }

public sealed record BaseDecodeSettings(HlReconstructionMode HlReconstruction, FbddMode NoiseReduction,
                                        bool Distortion, bool ChromaticAberration, bool Vignetting)
{
    public static BaseDecodeSettings Default { get; }        // Clip + Off + built-in profile
    public static BaseDecodeSettings From(EditSettings s);   // also carries rawProfile selection
    public string CacheKey { get; }                          // adds dcp=resolved token when selected
}

public sealed record BaseImageInfo(
    BaseSourceKind Kind,
    bool IsRawSource,              // true only for mosaic sources decoded via LibRaw
    BaseDecodeSettings Decode,     // the settings this base was decoded with
    double[]? CamMul,              // length 3, or native LibRaw length 4; null if unavailable
    double[,]? CamToSrgb,          // camera → linear sRGB, 3×CamMul.Length; null if unavailable
    double AsShotKelvin,           // measured for raw when facts exist; 6504 non-raw
    double AsShotTint,             // measured for raw when facts exist; 0 non-raw
    bool HadIccProfile,
    string? IccDescription,
    int ExifOrientationApplied,    // for diagnostics; pixels are already upright
    int FullWidth,                 // native full-resolution dimensions after orientation —
    int FullHeight,                // set on preview bases too; RENDER.md §9 scales σ by these
    double SourceExposureBiasEv = 0); // Fuji midpoint restoration; 0 for other sources
// IsMonochrome is a loader-produced fact propagated with preview/render outcomes.
// Also carries the generation-matched embedded-lens prescription summary.

public sealed class BaseImage : IDisposable
{
    public const int Version = …;        // current value lives in code; bump whenever decoded pixels or facts change
    public const int InteractivePreviewMaxDimension = 1600;
    public const int LargePreviewMaxDimension = 3200;
    public MagickImage Pixels { get; }   // Depth 16, ColorSpace RGB (linear), no profiles
    public BaseImageInfo Info { get; }
}

public sealed class PreviewBasePair : IDisposable
{
    public BaseImage Interactive { get; } // pre-derived slider base
    public BaseImage? Large { get; }       // bounded resting-render base
}

internal sealed record PreviewSourceAnalysis(
    HistogramData? RawHistogram,
    SourceSaturationMask? SourceSaturation);

internal sealed class PreviewBaseLease : IDisposable
{
    public BaseImage Base { get; }
    public PreviewSourceAnalysis Analysis { get; }
}

public enum RenderIntent { Preview, Export }
public enum OutputColorSpace { Srgb, DisplayP3 }

public sealed record RenderOptions(
    bool ComputeStats = true,
    bool ComputeOverlayMasks = false,
    ClippingOverlaySide OverlaySides = ClippingOverlaySide.Both);

public sealed record RenderRequest(
    BaseImage Base, EditSettings Settings, RenderIntent Intent,
    int? MaxDimension, RenderOptions Options,
    OutputColorSpace OutputColorSpace = OutputColorSpace.Srgb) // RENDER.md §1.1
{
    internal SourceSaturationMask? SourceSaturation { get; init; }
}

public sealed class RenderResult : IDisposable
{
    public MagickImage Image { get; }        // display-referred selected output, 16-bit
    public ClippingStats Clipping { get; }   // see RENDER.md §7
    public ClippingMask? OverlayMask { get; } // per-pixel side flags; only when Options.ComputeOverlayMasks
}
```

`BaseImageInfo` also carries the internal resolved DCP payload, outcome token,
typed status/message, and normalized camera identity. These facts install with
the characterized pixels; render never resolves or mixes profile state.

`EditSettings` v3 schema and current storage contract: RENDER.md §8.

Develop exposes capture sharpening, RAW-only FBDD noise reduction, and chroma noise
reduction in its Detail group. Capture sharpening displays its source-kind default
(RAW 25, standard 0) while persisting that default as `null`. Fit and 1:1 views both
use the bounded preview base, so detail fidelity is judged on export-scale renders;
native-detail inspection is not part of this viewer.

`BaseImage` exclusively owns `Pixels` after construction. Callers may hold a base across
multiple renders but must dispose it only after those renders finish; disposal is
idempotent and accessing `Pixels` afterward throws. A loader returning `null` retains
ownership of any temporary image it created. `BaseImageInfo` is loader-produced factual
metadata and consumers treat it as immutable. Preview loaders additionally return one
immutable `PreviewSourceAnalysis`: RAW derives both fields from the same unpacked-mosaic
pass; JPEG/HEIC derive only source saturation from upright encoded samples before color
normalization. The coordinator installs pair and analysis atomically and exposes them
only through a generation-matched lease, so stale work cannot mix artifacts with newer
pixels. Full/export loads return only canonical pixels and render-required facts and do
no histogram or saturation sampling. Source-saturation absence is the highlight-warning
capability signal.

## 5. Service map

| File | Role |
|------|------|
| `Services/BaseImage.cs` | `BaseImage`, `BaseImageInfo`, `BaseSourceKind`, `BaseDecodeSettings` |
| `Services/PreviewBaseCoordinator.cs` + `.Leases.cs` | single-flight current-pair ownership and generation-matched analysis leases |
| `Services/IBaseImageLoader.cs` + `BaseLoaderRouter.cs` | route by format |
| `Services/GatedBaseImageLoader.cs` | live availability policy before source decode |
| `Services/SourceAvailabilityService.cs` | cloud-file classification and read intent |
| `Services/RawBaseLoader.cs` | LibRaw decode → base (DECODE.md §2) |
| `Services/LensPrescription*.cs` | embedded DNG/Fuji prescription readers and model |
| `Services/LensCorrection*.cs` | fused camera-plane inverse map and linear gain |
| `Services/RawCameraFactSnapshot.cs` | validated pre-process camera facts |
| `Services/CameraRgbCharacterization.cs` | camera RGB → Rec.2020 fused Q16 import |
| `Services/DcpProfileReader.cs` + `DcpTiffReader.cs` | hardened TIFF-IFD DCP parser |
| `Services/DcpProfileService.cs` + `DcpProfileDiscovery.cs` | availability-gated resolution and lazy local discovery |
| `Services/DcpMatrixCalculator.cs` | as-shot DCP matrix composition for the balanced seam |
| `Services/DcpHueSatRenderer.cs` | scene-linear ProPhoto HueSatDeltas render stage |
| `Services/RawSensorFrame.cs` + `RawSensorHistogram.cs` | typed unpacked-mosaic lease + sensor histogram |
| `Services/SourceSaturationMask.cs` + `SourceSaturationMaskProjector.cs` | packed source-saturation artifact + geometry projection |
| `Services/StandardBaseLoader.cs` | Magick decode + ICC normalize → base (DECODE.md §3) |
| `Services/WorkingSpaceIccProfile.cs` | deterministic linear-Rec.2020 ICC target |
| `Services/OutputColorProfiles.cs` | embedded sRGB / Display P3 export profiles |
| `Services/RgbColorSpaceMatrices.cs` | authoritative published/exact RGB↔XYZ matrices |
| `Services/RenderPipeline.cs` | stage orchestration and result ownership (RENDER.md) |
| `Services/RenderGeometry.cs` | rotation, horizon correction, and crop |
| `Services/AgxCrossing.cs` + `AgxToneEngine.cs` | RAW inset → tone engine → outset crossing |
| `Services/AgxToneLut.cs` | exact crossing-on tone table and bounded settings cache |
| `Services/ToneLut.cs` | exact crossing-off LUT composition (RENDER.md §5) |
| `Services/ToneLutApplicator.cs` | unrounded-input linear interpolation with one Q16 write |
| `Services/RenderChromaticStage.cs` | white-balance matrix application |
| `Services/RenderChromaStage.cs` + `OklabColor.cs` | fused OKLCh saturation/vibrance and gamut projection |
| `Services/RenderDetail.cs` + `RenderSharpening.cs` | fixed detail operations |
| `Services/RenderEffects.cs` | post-resize vignette and deterministic film grain |
| `Services/WhiteBalanceModel.cs` | CCT/tint ↔ gains math (WHITE_BALANCE.md) |
| `Services/ChromaticAdaptation.cs` | Bradford matrices, normalization |
| `Services/ClippingStats.cs` | clip counters + overlay masks |
| `Services/ExportMetadataService.cs` | EXIF copy/strip policy (OUTPUT.md §4) |

`IRawProcessingService` is intentionally outside this base/render path. It extracts
encoded thumbnails and metadata for browsing through the same versioned Happy Photon
bridge and RID-selected `HappyPhoton.LibRaw.Native` 0.22.2.11 package. A rejected runtime
disables RAW decoding until repaired; Magick does not decode RAW raster pixels.

## 6. Pipeline versioning

`RenderPipeline.Version` participates in the settings hash used by the rendered-preview
cache (DECODE.md §5) and selects the matching golden baseline. A visible render-math
change increments it, which invalidates rendered caches and makes the corresponding
golden update explicit. Decode changes increment `BaseImage.Version` similarly.
An additive optional setting is the narrow exception: the version stays put when
omitting the new field reproduces old pixels bit-for-bit and canonical JSON omits it,
so old settings hashes and caches remain valid. Per-channel curves and the nullable
effects object use this exception; present pixel-active fields change the canonical
settings hash.
`BaseDecodeSettings.CacheKey` is the invariant, culture-independent string
`base-v{BaseImage.Version};hl={blend|clip};fbdd={off|light|full};lens={ddd}`, with
`;dcp={source:content-hash:resolution-status}` appended only for a selected profile.
In-memory identity adds normalized file path and preview/full size class;
rendered-cache settings hashes also carry the installed outcome token.
The current marker values live in code (`RenderPipeline.Version` and
`BaseImage.Version`); the doc deliberately does not restate them. The
perceptual OKLCh chroma stage incremented the render version (its golden
attribution lives beside the goldens); a selected DCP profile bumps neither
version: the no-profile path is unchanged, while active and rejected profile
outcomes are isolated by the DCP token.

## 7. Current boundaries

Local adjustments/masks, perspective corrections, custom output ICC targets,
display-profile awareness, XMP sidecars, HDR output, AVIF/JXL, 1:1-zoom region decode
(zoom continues to use the bounded preview base). These are product boundaries, not
partially implemented pipeline stages.
