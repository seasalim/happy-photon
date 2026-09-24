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
  (highlight reconstruction, optics toggles, and camera-profile selection — all raw-only). Everything
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
   intent.
10. **Adjacent warming is cache-only speculation.** A bounded worker uses only locally
    readable neighbors and retains no base or source analysis. Its results can reach
    the surface only through source- and settings-matched rendered-cache outcomes.

## 3. Stage diagram

```text
RAW → LibRaw camera RGB → optics/sample → characterize ┐
Monochrome RAW → linear gray resize → replicate RGB    ├→ BaseImage
Standard → Magick decode → color normalization        ┘  linear Rec.2020 Q16

BaseImage → geometry → optional DCP HueSat → WB/locals → tone regime
          → chroma → detail → linear resize → output sharpen → vignette/grain
          → sRGB/P3 convert → clamp/encode → display scopes/clipping
          → bitmap or tagged export
```

RAW tone uses AgX inset/LUT/outset; standard tone retains the display-domain chain.
RENDER.md owns exact ordering and math; OUTPUT.md owns finalization and encoding.

Interactive and export-statistics analysis derive clipping, overlay flags and scope
BGRA8 in one pass over the final encoded Q16 array (RENDER.md §7).

## 4. Runtime contracts

Runtime types live in [BaseImage.cs](../../Services/BaseImage.cs) and
[RenderContracts.cs](../../Services/RenderContracts.cs). `BaseDecodeSettings` projects
the decode-affecting settings; `BaseImageInfo` carries loader-produced facts.
[PreviewBasePair.cs](../../Services/PreviewBasePair.cs) defines the interactive/large
pair;
[PreviewBaseCoordinator.Leases.cs](../../Services/PreviewBaseCoordinator.Leases.cs)
defines `PreviewSourceAnalysis` and its generation-matched `PreviewBaseLease`.
`RenderRequest` supplies the base, edits, intent, output target, sharpening and
auxiliary-work options; `RenderResult` owns the rendered image and optional
scopes/masks. The internal locals-frame override preserves corrected-frame coordinates
in resting work.

`EditSettings` uses the v4 schema (RENDER.md §8). Version 3 migrates in memory;
version 2 is rejected by `EditSettingsJson`.

`BaseImage` exclusively owns immutable pixels; callers keep it alive through renders
and dispose it afterward. Loaders own temporary images on failure. Loader facts are
immutable. Preview pair and source analysis install atomically and are accessible only
through generation-matched leases, preventing facts from mixing with newer pixels.
RAW preview derives histogram/saturation in one mosaic pass; standard preview captures
supported source saturation before normalization. Full loads omit both. Missing source
saturation signals unavailable highlight warnings. DECODE.md §4 owns lease mechanics.

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
| `Services/RenderGeometry.cs` + `RenderGeometryMap.cs` + `GeometryWarpProcessor.cs` | lossless quarter turns, fused corrected-frame warp, and crop |
| `Services/AgxCrossing.cs` + `AgxToneEngine.cs` | RAW inset → tone engine → outset crossing |
| `Services/AgxToneLut.cs` | exact crossing-on tone table and bounded settings cache |
| `Services/ToneLut.cs` | exact crossing-off LUT composition (RENDER.md §5) |
| `Services/ToneLutApplicator.cs` | unrounded-input linear interpolation with one Q16 write |
| `Services/RenderChromaticStage.cs` | white-balance matrix application |
| `Services/RenderLocals.cs` | fused local exposure/color and range evaluation |
| `Services/LocalRangeSampling.cs` | shared pre-tone classification seam |
| `Services/LocalRangeMaskRenderer.cs` | display-only range-mask rendering |
| `Services/RenderChromaStage.cs` + `OklabColor.cs` | fused OKLCh saturation/vibrance and gamut projection |
| `Services/RenderNoiseReduction*.cs` + `RenderSharpening.cs` | wavelet noise reduction and fixed sharpening operations |
| `Services/RenderEffects.cs` | post-resize vignette and deterministic film grain |
| `Services/WhiteBalanceModel.cs` | CCT/tint ↔ gains math (WHITE_BALANCE.md) |
| `Services/ChromaticAdaptation.cs` | Bradford matrices, normalization |
| `Services/ClippingStats.cs` | clip counters + overlay masks |
| `Services/ExportMetadataService.cs` | EXIF copy/strip policy (OUTPUT.md §4) |

`IRawProcessingService` is intentionally outside this base/render path. It extracts
encoded thumbnails and metadata for browsing through the same versioned Happy Photon
bridge and RID-selected `HappyPhoton.LibRaw.Native` 0.22.2.12 package. A rejected runtime
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
`base-v{BaseImage.Version};hl={blend|clip};lens={ddd}`, with
optional `;lens-profile={escaped model}` and
`;dcp={source:content-hash:resolution-status}` suffixes for selected lens/camera profiles.
In-memory identity adds normalized file path and preview/full size class;
rendered-cache settings hashes also carry the installed outcome token.
The current marker values live only in `RenderPipeline.Version` and
`BaseImage.Version`. Selected DCP outcomes are isolated by their resolved token.

## 7. Current boundaries

Linear, radial and brush local adjustments ship. Remaining boundaries are layered compositing,
custom output ICC profiles, HDR output, AVIF/JXL, and 1:1 region decode (zoom uses the
bounded preview base). XMP exchanges assessments only, not develop settings.
