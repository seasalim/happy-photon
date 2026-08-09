# Decode: Sources to `BaseImage`

Every supported file becomes the same canonical `BaseImage`: linear light, sRGB
primaries, D65, Q16, upright, and free of an aesthetic look. That normalization is
what lets preview and export share one source-agnostic renderer. See OVERVIEW.md §4
for the runtime contracts.

## 1. Loader routing

```csharp
public interface IBaseImageLoader
{
    bool CanLoad(ImageFile file);
    BaseImage? LoadPreviewBase(ImageFile file, BaseDecodeSettings decode, CancellationToken ct); // ~1600px class
    BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken ct);    // native resolution
}
```

`BaseDecodeSettings` (OVERVIEW.md §4) carries the decode-affecting subset of
`EditSettings` — highlight reconstruction and FBDD. Non-raw loaders accept and record
it but ignore it (their `From(EditSettings)` projection is still stored on
`BaseImageInfo.Decode` so cache keys stay uniform). The defaults are highlight clip
and FBDD off.

`BaseLoaderRouter` picks the loader:

- Mosaic RAW extensions (`.CR2 .CR3 .NEF .NRW .ARW .SRF .SR2 .DNG .RAF .ORF .RW2 .PEF`)
  route to `RawBaseLoader`. When native LibRaw is unavailable or decoding fails, the
  router logs the fallback and tries `StandardBaseLoader`; RAF deliberately has no
  Magick fallback on Windows.
- `.HEIC .HEIF` route to `StandardBaseLoader` with `Kind = HeicPlatform`. They are
  standard image sources rather than RAW files, including in the thumbnail path.
- Everything else (`.JPG .JPEG .PNG .BMP .GIF .TIFF .WEBP`) → `StandardBaseLoader`.

## 2. `RawBaseLoader` (LibRaw via Sdcb.LibRaw)

`RawBaseLoader` configures LibRaw explicitly so decoded pixels do not depend on its
image-statistics defaults:

| Param | Value | Why |
|-------|-------|-----|
| `OutputBps` | **16** | keep sensor precision |
| `Gamma` | **(1.0, 1.0)** | linear output; encode happens in the tone LUT |
| `NoAutoBright` | **true** | determinism — kills the per-image 1% stretch |
| `UseCameraWb` | true | as-shot neutral becomes (1,1,1); WB edits are relative gains |
| `OutputColor` | sRGB (1) | LibRaw applies its camera matrix → sRGB primaries |
| `HighlightMode` | **0 (clip)** when `decode.HlReconstruction == Clip` (default); 2 when `Blend` | Blend recovers neutral detail in partially clipped areas; output stays ≤ 1.0 |
| `fbdd_noiserd` | 0/1/2 from `decode.NoiseReduction` (default Off) | internal decode-time NR |
| `HalfSize` | true for `LoadPreviewBase`, false for `LoadFullBase` | perf; only remaining preview/export decode difference |

The loader applies no S-curve, saturation boost, or 8-bit conversion. The optional
base look belongs to the render LUT (RENDER.md §5.4), where it remains explicit and
identical between preview and export.

Post-decode steps, in order:

1. The `MagickImage` is constructed by **direct pixel import** from the LibRaw
   native-endian output span. The loader creates a blank Q16 destination, tags it as
   `ColorSpace.RGB`, then calls
   `ImportPixels(data, new PixelImportSettings(w, h, StorageType.Short, PixelMapping.RGB))`.
   Tagging the destination before import is essential: assigning `sRGB → RGB` after
   samples exist runs Magick's transfer transform and double-linearizes the already
   linear LibRaw values. There is no PPM intermediate or managed full-image copy.
   Synthetic-buffer tests pin byte order and exact 16-bit preservation. The former
   PPM wrapping path is not used by the base loader.
2. LibRaw sometimes pre-rotates. The loader detects that through the dimension swap,
   applies EXIF orientation otherwise, and records `ExifOrientationApplied`.
3. Preview bases use LibRaw's half-size decode and are bounded to
   `PreviewMaxDimension` (1600) with Lanczos. Full bases remain native resolution.
4. `BaseImageInfo` stores the raw facts — either RGB `CamMul[3]` with
   `CamToSrgb[3][3]`, or native LibRaw `cam_mul[4]` with `rgb_cam[3][4]` — from the
   wrapper's color data where exposed, else null. Preserve all four native channels;
   do not silently truncate the second green or other camera color. Also set
   `FullWidth/FullHeight` = the native full-resolution, orientation-applied dimensions
   (known from LibRaw sizes even for a half-size preview decode — RENDER.md §9 needs
   them for σ scaling). Set `AsShotKelvin/Tint` with
   `WhiteBalanceModel.EstimateAsShot(CamMul, CamToSrgb)`, falling back to 5500 / 0
   when either camera fact is unavailable or invalid. Treat an identity `rgb_cam`
   as LibRaw's unavailable-transform sentinel rather than projecting camera-space
   multipliers as linear sRGB (WHITE_BALANCE.md §5).

### 2.1 RAW Library previews

Library thumbnail extraction uses LibRaw's already-open context to return both the
encoded embedded preview and `ctx.Width`/`ctx.Height`, the visible dimensions rendered
by Develop. Camera-wide aspect differences are treated as preview padding: differences
at or below 3% pass through, while larger differences center-crop the embedded preview
toward the visible RAW aspect before the requested generation-size resize. Missing or
non-positive visible dimensions disable normalization but do not reject successfully
decoded preview bytes.
A valid LibRaw preview is never rejected on geometry grounds, so a camera-wide mismatch
cannot fall through to the preview-frame path and invoke Magick's RAW delegate during a
Library load.

This policy is specific to LibRaw. EXIF thumbnails continue to reject missing geometry
and mismatches above 3%. Embedded-JPEG and preview-frame fallbacks remain unnormalized,
and existing cached thumbnails are not migrated or invalidated. Extraction retains the
largest safe candidate seen, continues while it is below the generation target, and
never starts a full RAW demosaic merely to satisfy a larger Library request.

### 2.2 Raw exposure

RAW decode leaves the linear pixels bias-free while recording a default-brightness
estimate in `BaseImageInfo.SourceExposureBiasEv`. The loader reads LibRaw's selected
embedded thumbnail from the already-open context, normalizes it to linear sRGB, and
compares both images on a 48px-long-edge linear sampling grid. If the
preview and base aspect ratios differ by more than 2%, the base is center-cropped to
the preview ratio before comparison. This is deliberately the opposite crop direction
from Library normalization, which crops the embedded preview toward the visible RAW
frame. A bounded solver then finds the scalar EV whose default raw transfer matches the
preview median.

The preview estimate is accepted only for thumbnails at least 64×64 with finite,
non-degenerate medians. A Fuji estimate that differs from its nonzero MakerNote bias
by more than 0.5 EV is also rejected: a high-dynamic-range scene can make one scalar
median track bright architecture while missing the camera curve's intended mid-tone
lift. Missing, corrupt, too-small, or rejected previews fall back to the Fujifilm
mid-point shift from MakerNote tag 0x9650, then to the RAF DR200/DR400 mode when that
tag is absent; all remaining sources fall back to 0. Every path is clamped to ±3 EV.
Preview and full decodes estimate independently and may differ by up to
0.05 EV because their LibRaw demosaics are approximate rather than identical. The
renderer combines the selected source fact with the user's relative Exposure setting
inside the tone LUT gain.

### 2.3 Why Clip and Blend are the supported modes

The LibRaw evaluation compared modes 0 (clip), 2 (blend), and rebuild levels 3, 5, and
9 on `Tests/assets/canon-eos-350d.cr2`. The fixture's clipped area is the bright
water reflection, not a sky. On the full 3474×2314 decode, clip left 232,878
clipped channel samples while blend left none. Rebuild levels left 8,907–16,476
clipped samples and raised mean chroma in the bright-mask region from 0.08565
for blend to 0.24421–0.25651. Visual comparison showed the corresponding
yellow-green false color across the reflection.

Rebuild is therefore excluded from the product surface. Deterministic `Clip` is the
default and `Blend` is the explicit recovery alternative. The comparison remains
reproducible from the repository root:

```powershell
dotnet run --file scripts/evaluate-highlight-reconstruction.cs -- `
  Tests/assets/canon-eos-350d.cr2
```

### 2.4 Platform runtime

Windows and Linux use the pinned Sdcb native runtime packages. Apple Silicon macOS
ships a LibRaw 0.21.1 `libraw.23.dylib` under `runtimes/osx-arm64/native/`; the project
copies it beside the app executable, where Sdcb.LibRaw 0.21.1.7's assembly-level
`NativeLibrary.SetDllImportResolver` maps the wrapper's `libraw.dll` imports to that
file. The dylib statically includes libjpeg-turbo 2.1.5.1 for lossy-compressed DNG and
has only macOS system-library dependencies. `LibRawNativeSupport` probes the native
version once, and both base decoding and thumbnail/metadata routing fall back to
Magick.NET only when the probe fails.

The same loader parameters and golden fixtures cover Windows, Linux, and macOS. The
cross-platform comparison uses the mean ΔE bound documented in TESTING.md §3.

## 3. `StandardBaseLoader` (Magick.NET)

1. JPEG sources are pinged for native geometry before decoding. `LoadPreviewBase` uses the
   `jpeg:size` hint at `2 × PreviewMaxDimension` only when the native long edge exceeds
   that hint, then resize to the preview bound (preserves quality through DCT-scaled
   decode without upscaling smaller JPEGs).
2. `AutoOrient()` makes the pixels upright and the applied orientation is recorded.
3. **Color normalization:**
   - Embedded ICC present → record its description, then
     `TransformColorSpace(<embedded>, sRGB)` (Magick applies the source profile and
     converts).
   - No profile + CMYK colorspace → assume `ColorProfiles.USWebCoatedSWOP` as the
     deterministic source profile and transform to sRGB.
   - No profile otherwise → assume sRGB (document; industry default).
   - Record `HadIccProfile` and the profile description, then strip **all** profiles
     after color conversion. Bases never retain ICC, EXIF/GPS, XMP, or thumbnails.
4. Ensure `Depth = 16` **before** linearization (8-bit sources are promoted so the
   linear representation doesn't posterize).
5. Linearize: `ColorSpace = ColorSpace.RGB` (Magick's sRGB→linear transfer transform).
6. Preview base: resize to 1600 as above.
7. `AsShotKelvin = 6504, AsShotTint = 0` (D65 anchor), `CamMul = null`;
   `FullWidth/FullHeight` = the original decoded dimensions after orientation
   (captured before the preview resize in step 6).

GIF decoding uses the first frame only. HEIC follows the identical standard path
through Magick.NET's HEIC coder backed by the libheif bundled in the package's native
binary for each target RID (win-x64, linux-x64, osx-arm64); it does not call Windows
HEIF Image Extensions directly. Gate HEIC tests first on
`MagickFormatInfo.Create(MagickFormat.Heic)?.SupportsReading`, then on an actual fixture
decode so delegate/package gaps are explicit skips. The base depth is whatever the
bundled codec yields; 10-bit sources may flatten to 8-bit before the Q16 promotion and
remain a documented limitation.

## 4. Ownership and concurrency

- `PreviewBaseCoordinator` owns **one in-memory `BaseImage` snapshot** for the current
  image. Base identity is **(normalized file path, `BaseDecodeSettings.CacheKey`, size
  class)**, so tonal, chroma, and geometry changes reuse the held base.
- **Single-flight, newest-wins decodes:** at most one decode in flight per identity;
  a newer request (image switch, decode-settings change) cancels/supersedes it and
  stale results are disposed, mirroring the existing thumbnail-session pattern.
- **Only `BaseDecodeSettings` changes re-decode.** While a replacement decode is in
  flight, preview renders lease the held old base. The newest settings accumulate, and
  completion emits one refresh using that latest state rather than a render backlog.
- `RenderPipeline` never mutates the held base (OVERVIEW invariant 8); it clones
  internally. Disposal of a superseded base happens only after any in-progress render
  against it completes (generation check, as with today's late thumbnail results).
- Export always calls `LoadFullBase` fresh per image (no base persistence); the render
  then runs once and writes all variants (OUTPUT.md §2).

## 5. Disk caches

- **Thumbnail cache:** `assets/thumbs/` contains one largest-wins unedited
  embedded/source JPEG per catalog image and uses a capacity-256 bounded writer. Cache
  dimensions come from a bounded JPEG SOF-header read before pixel decode. Existing
  150px entries satisfy Small and Medium; cache misses generate 150px, 192px, or 512px
  for Small, Medium, or Large. An undersized entry paints as a placeholder while a safe
  source upgrade is queued. It is also the only input to agent image statistics, which
  normalize every input to a canonical 150px long edge.
- **Rendered-preview cache:** `PreviewCacheService` stores the *last rendered output*
  (8-bit JPEG q90, 1600px)
  plus a sidecar `<id>.meta` containing `settingsHash`.
  - `settingsHash` = SHA-256 of canonical-JSON `EditSettings` v2 + `RenderPipeline.Version`
    + `BaseImage.Version`.
  - Develop entry: if cached hash matches current settings → paint instantly, decode base
    in background for subsequent edits. Hash mismatch → paint it anyway as a stale
    placeholder while base decode + fresh render replace it (no flash of nothing).
  - Existing atomic-write, bounded-channel, drop-oldest, 2 s drain rules all carry over.
  - Write policy: queue a cache write only on leaving the image (or a long debounce),
    never per slider settle — an edit session must not multiply write traffic.
- **Rendered RAW thumbnail cache:** `assets/rendered-thumbs/` stores one largest-wins
  q85 output per settings hash from accepted edited RAW Develop renders, capped at
  512px. Its versioned metadata sidecar stores the deterministic settings hash and
  raster dimensions; legacy plain-hash sidecars infer dimensions from the JPEG. The
  cache uses an independent capacity-8 writer so folder scans cannot evict
  active-session promotion. An accurate undersized match remains visible instead of
  falling through to a sharper source thumbnail that would omit tone and color edits.
  Cache misses use the embedded source thumbnail with geometry only; they never trigger
  a RAW base decode or load the 1600px preview.
- **Linear base disk cache:** deliberately absent. Bases are retained only in memory;
  the rendered JPEG cache supplies immediate paint while a base is armed.

## 6. Error handling

Loader failures are logged through `ImageServiceHelpers` and return null to the UI's
existing load-error path. A RAW file decoded by the standard fallback reports
`Kind = Standard, IsRawSource = false`, so downstream capability checks remain honest.

## 7. Verification

- Decoding the same file twice (any mix of preview/full) yields identical bases modulo
  resolution (golden ΔE ≈ 0 for full-vs-full; documented tolerance half-vs-full).
- A P3-tagged and an sRGB-tagged encode of the same picture produce near-identical bases
  (ΔE tolerance, TESTING.md §4.3).
- No base pixel depends on image statistics (burst determinism test).
- HEIC preview no longer attempts LibRaw (assert via debug log capture).
