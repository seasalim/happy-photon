# Decode: Sources to `BaseImage`

Every supported file becomes the same canonical `BaseImage`: linear light, Rec.2020
primaries, D65, Q16, upright, and free of an aesthetic look. That normalization is
what lets preview and export share one source-agnostic renderer. See OVERVIEW.md §4
for the runtime contracts.

## 1. Loader routing

```csharp
public interface IBaseImageLoader
{
    bool CanLoad(ImageFile file);

    BaseImageLoadOutcome LoadPreviewBaseWithOutcome(          // preview pair
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken);

    BaseImage? LoadFullBase(                                  // native resolution
        ImageFile file,
        BaseDecodeSettings decode,
        CancellationToken cancellationToken);
}
```

`LoadPreviewBase` is an extension method (`BaseImageLoaderExtensions`) that calls
`LoadPreviewBaseWithOutcome` and detaches the interactive image.

`BaseDecodeSettings` (OVERVIEW.md §4) carries the decode-affecting subset of
`EditSettings` — highlight reconstruction, FBDD, optics toggles, and the selected camera profile.
Non-raw loaders accept and record
it but ignore it (their `From(EditSettings)` projection is still stored on
`BaseImageInfo.Decode` so cache keys stay uniform). The defaults are highlight clip
and FBDD off with built-in characterization.

`BaseLoaderRouter` picks the loader:

- Mosaic RAW extensions (`.CR2 .CR3 .NEF .NRW .ARW .DNG .RAF .ORF .RW2 .PEF`)
  route only to `RawBaseLoader`. `StandardBaseLoader` rejects these extensions in both
  its capability and load paths. A rejected native runtime emits one process-level
  diagnostic; a file LibRaw cannot decode is reported as unsupported in Library and
  Develop. Neither failure can route RAW pixels through Magick.
- `.HEIC .HEIF` route to `StandardBaseLoader` with `Kind = HeicPlatform`. They are
  standard image sources rather than RAW files, including in the thumbnail path.
- Everything else (`.JPG .JPEG .PNG .BMP .GIF .TIFF .WEBP`) → `StandardBaseLoader`.

## 2. `RawBaseLoader` (LibRaw via the Happy Photon bridge)

`RawBaseLoader` configures LibRaw explicitly so decoded pixels do not depend on its
image-statistics defaults:

| Param | Value | Why |
|-------|-------|-----|
| `OutputBps` | **16** | keep sensor precision |
| `Gamma` | **(1.0, 1.0)** | linear output; encode happens in the tone LUT |
| `NoAutoBright` | **true** | determinism — kills the per-image 1% stretch |
| `UseCameraWb` | true | as-shot neutral becomes (1,1,1); WB edits are relative gains |
| `UseCameraMatrix` | true | preserve open-time camera/DNG fact population; no matrix is applied when output color is 0 |
| `OutputColor` | camera-native (0) | skip LibRaw output-space conversion; characterize in-app |
| `HighlightMode` | **0 (clip)** when `decode.HlReconstruction == Clip` (default); 2 when `Blend` | Blend recovers neutral detail in partially clipped areas; output stays ≤ 1.0 |
| `fbdd_noiserd` | 0/1/2 from `decode.NoiseReduction` (default Off) | internal decode-time NR |
| `HalfSize` | true for `LoadPreviewBase`, false for `LoadFullBase` | perf; only remaining preview/export decode difference |

True monochrome RAW is the single exception to the color-output rows above. The
loader classifies only `LibRawSensorIdentity.Colors == 1`, then requires a one-channel
processed image (ordinary RAW still requires three channels). It requests
camera-native linear output with camera/auto WB and the camera matrix disabled, unit
user multipliers, and no half-size decode. Every classification/layout mismatch is
unsupported.

The loader applies no S-curve, saturation boost, or 8-bit conversion. RAW picture
formation belongs to the AgX crossing (TONE_ENGINE.md); no base look is applied to
crossing-on sources.

Post-decode steps, in order:

1. LibRaw's AHD Bayer and Markesteijn X-Trans paths both end at the same linear,
   neutralized, normalized three-channel camera-RGB span. `CameraRgbCharacterization`
   composes camera→sRGB with the exact sRGB→Rec.2020 matrix in `double`, then writes
   through Magick's writable Q16 cache pointer: camera `ushort` → matrix → one rounded
   Q16 write, with the only clamp at that write (the uncharacterized outcome imports
   the native codes unchanged). The destination is tagged `ColorSpace.RGB` before any
   sample is stored, and the loader recycles the LibRaw context before import so raw
   and processing buffers do not overlap the render allocation — no PPM, intermediate
   full-frame pass, managed full-image copy, or second Magick pixel cache. When a DCP
   resolved successfully and WB facts are valid, its balanced-seam ForwardMatrix or
   ColorMatrix characterization replaces the built-in matrix in this same fused import,
   with CameraCalibration, AnalogBalance, and the applicable matrix pair interpolated
   at the as-shot CCT; missing-WB and all typed profile rejections use the unchanged
   built-in transform, and the matching HueSat payload and outcome token attach to
   `BaseImageInfo` at the same installation boundary. Four-channel processed output is
   rejected as unsupported rather than truncated. The binding defaults OpenMP to at
   most sixteen workers unless the process defines `OMP_NUM_THREADS`, bounding X-Trans
   scratch space without changing decode precision or pixels.
   With an active embedded prescription, this import is OPTICS.md's fused inverse map:
   it samples camera planes per channel, applies scene-linear vignetting gain, and only
   then runs the characterization matrix. Inactive paths retain the former pixels.
2. LibRaw sometimes pre-rotates. The loader detects that through the dimension swap,
   applies EXIF orientation otherwise, and records `ExifOrientationApplied`.
3. Preview uses one LibRaw half-size decode. Two bases derive independently from that
   decoded buffer: interactive is the same one-step linear resize to 1600 as before;
   large is one resize to min(half-size result, 3200). Small sensors are never upscaled
   and preview never forces a full decode. Full bases remain native resolution.
   Active optics writes each preview target directly rather than resizing a corrected
   intermediate; an active full base receives its single budgeted warp pass.
4. `BaseImageInfo` stores the raw facts — either RGB `CamMul[3]` with
   `CamToSrgb[3][3]`, or native LibRaw `cam_mul[4]` with `rgb_cam[3][4]` — from the
   wrapper's color data where exposed, else null. Preserve all four native channels;
   do not silently truncate the second green or other camera color. Also set
   `FullWidth/FullHeight` = the native full-resolution, orientation-applied dimensions
   (known from LibRaw sizes even for a half-size preview decode — RENDER.md §9 needs
   them for σ scaling). Measure `AsShotKelvin/Tint` by projecting
   `pre_mul / cam_mul` through `rgb_cam`; use 5500 / 0 only when a required fact is
   absent. Bridge ABI v2 also exposes `cam_xyz` and per-channel `linear_max`.
   Characterization uses `cam_xyz` only to derive the built-in transform when
   `rgb_cam` is LibRaw's identity unavailable-transform sentinel; `linear_max` remains
   typed at the interop boundary until a consumer needs it (WHITE_BALANCE.md §5).

For a monochrome preview, the loader area-averages the gray Q16 plane to the 3200-pixel
large-preview bound while LibRaw owns it, releases the native plane, and only then
replicates gray into the existing RGB base; the pair factory derives the 1600-pixel
interactive base. Full/export loads replicate at native resolution in bounded bands,
without a full-resolution managed RGB staging buffer. All three destination samples
receive the same code. `BaseImageInfo.IsMonochrome` is set, camera and DCP facts are
absent, and profile characterization is ignored; the 5500 K fallback remains an
informational as-shot value.

Camera facts are copied immediately after `Unpack`, before the camera-native output
configuration is applied. `CamToSrgb` therefore remains camera→linear-sRGB and is not a
camera→working-space fact. `RawWorkingSpaceTests` pins the semantics against the
separately exposed camera-from-XYZ fact: row-normalize
`camera_from_xyz · (sRGB→XYZ)`, then invert it to reproduce `camera_to_srgb`.

For preview loads at the same seam, after camera-fact copying and before
`ConfigureOutput`/`Process`, `RawSensorFrame` combines the typed bridge sensor identity with a zero-copy
`BorrowMosaic` lease, always released before `Process` (a held lease intentionally
makes native process/recycle calls reject). `RawSensorHistogram` then scans the
visible photosites once, synchronously on the decode call and token, producing both
the aggregate histogram and a packed preview-size per-channel saturation artifact
with the exact same `value >= maximum` predicate. Rows are chunked
across parallel workers whose per-worker bins merge into order-independent integer
sums, so the histogram is bit-identical for any worker count. Cancellation is checked
every 256 visible rows and immediately before processing. Cancellation escapes the
loader; any other sampling/access fault is logged once and leaves a valid decoded pair
with empty source analysis. Only integer CFA mosaics described by Bayer `filters > 1000` or
the 36-byte X-Trans table (`filters == 9`) qualify; anything else — no-CFA, Leaf and
other filter tables, invalid geometry/levels, bridge-unavailable mosaics — returns no
RAW histogram. There is no second decode, source reread, or processed-RGB substitute.
The mask maps the visible sensor window by ratio to the oriented decoded dimensions
with OR reduction. The histogram and interactive-size mask travel together in the
immutable `PreviewSourceAnalysis` installed beside the pair; full/export loads skip
the entire sampling pass.

### 2.1 RAW Library previews

Library thumbnail extraction uses LibRaw's already-open context to return both the
encoded embedded preview and `ctx.Width`/`ctx.Height`, the visible dimensions rendered
by Develop. Aspect differences at or below 3% pass through as preview padding; larger
differences center-crop the embedded preview toward the visible RAW aspect before the
generation-size resize. Missing or non-positive visible dimensions disable
normalization but never reject successfully decoded preview bytes. If the result stays
undersized, Library may try metadata-only EXIF extraction and a byte-level
embedded-JPEG scan, but never opens the RAW container through Magick.

This policy is specific to LibRaw: EXIF thumbnails still reject missing geometry and
mismatches above 3%, and embedded-JPEG candidates remain unnormalized. Extraction
retains the largest safe candidate seen, continues while it is below the generation
target, and never starts a full RAW demosaic merely to satisfy a larger Library request.

### 2.2 Raw exposure

RAW decode leaves the linear pixels bias-free while recording a default-brightness
estimate in `BaseImageInfo.SourceExposureBiasEv`. The loader reads LibRaw's selected
embedded thumbnail from the already-open context, normalizes it to display sRGB, and
compares both images on a 48px-long-edge linear sampling grid; if the preview and base
aspect ratios differ by more than 2%, the base is center-cropped to the preview ratio
first (deliberately the opposite crop direction from Library normalization). A bounded
solver then finds the scalar EV whose neutral AgX render matches the preview median,
with base samples passing through the same default inset → log2/sigmoid → outset
crossing as the renderer, including the Rec.2020-to-sRGB comparison basis.

The preview estimate is accepted only for thumbnails at least 64×64 with finite,
non-degenerate medians. A Fuji estimate is clamped to within 0.5 EV of its nonzero
MakerNote bias; an estimate without a metadata anchor is clamped to ±1 EV around zero.
The latter bound prevents a high-key camera preview from turning its highlight-heavy
median into a large global mid-tone lift. Clamping rather than rejecting keeps the
selection continuous: repeated decodes of the same file measure a few hundredths of
an EV apart, so a hard accept/reject threshold flipped files sitting at the boundary
by the full disagreement between decodes of different noise-reduction modes. Missing,
corrupt, or too-small previews fall back to the Fujifilm mid-point shift from
MakerNote tag 0x9650, then to the RAF DR200/DR400 mode when that tag is absent; all
remaining sources fall back to 0. Metadata-anchored paths remain bounded to ±3 EV.
Preview and full
decodes estimate independently and may differ by up to 0.05 EV because their LibRaw
demosaics are approximate rather than identical. The renderer combines the selected
source fact with the user's relative Exposure setting inside the tone-engine gain, so
estimator and engine share the anchored post-gain quantity `a = v·2^(EVuser+EVsource)`;
an unusable preview falls back to a defensible decoded fact and never changes slider
semantics.

### 2.3 Why Clip and Blend are the supported modes

LibRaw's rebuild levels are excluded from the product surface: on the evaluation
fixture they left residual clipped samples and introduced strong false color, while
blend cleared the clipping without either. Deterministic `Clip` is the default and
`Blend` is the explicit recovery alternative. The measured comparison is reproducible
via `scripts/evaluate-highlight-reconstruction.cs`.

### 2.4 Platform runtime

All supported platforms use `HappyPhoton.LibRaw.Native` 0.22.2.12 through the managed
binding; NuGet selects the matching RID assets. The binding resolves the bridge and its
LibRaw 0.22.2 companion from one package-local directory by absolute path — never a
system or PATH copy. `LibRawNativeSupport` performs one process-wide health probe
requiring bridge ABI 4, numeric LibRaw version `0x001602` exactly, and LibRaw's JPEG
and zlib capability bits; an ABI mismatch stops before the versioned runtime structure
is queried. Rejections retain bridge-versus-companion attribution, record the safely
observed ABI, version, version string, and capability mask, and emit one error-level
diagnostic. RAW decode and LibRaw preview/metadata extraction stay unavailable until
the installation is repaired; the About surface reports the degraded state and includes
the same facts in copied support text. Header-only RAW `Ping`, EXIF thumbnail
extraction, orientation reads, and decoding already-extracted preview bytes remain
permitted because they do not decode the RAW raster.

Bridge ABI 4 adds one header-stage lens-identity read over LibRaw's parsed generic
maker-note lens block. It carries the composite ID, maker-note name, lens/camera mount
and format facts, focal/aperture ranges, and teleconverter, adapter, and attachment
identifiers without decoding raster data or adding maker-specific native parsing.
Managed Nikon resolution is data-driven; makers without a shipped table retain only a
transmitted maker-note name.

The bridge also exposes a mutable mosaic lease whose writes are consumed by the following
LibRaw process call, plus optional `user_sat`, named `user_qual` requests, and an
accept-only-verbatim full-resolution crop box. These are interop capabilities only:
`RawBaseLoader` does not use them until a pipeline consumer defines the corresponding
decode contract, and an absent quality or crop restores LibRaw's own sentinel, keeping
the zero-initialized configuration pixel-identical.

The same loader parameters and golden fixtures cover Windows, Linux, and macOS; the
cross-platform comparison uses the mean ΔE bound documented in TESTING.md §3.

### 2.5 Single RAW decoder decision (2026-08-16)

There is no Magick RAW fallback: Magick.NET's RAW support is itself LibRaw — an older,
slower, unaudited build invisible to the native health gate, whose pixels are not
interchangeable with the audited 0.22.2 runtime in shared caches. Every RAW raster
producer is LibRaw 0.22.2, enforced by construction: no production route decodes a RAW
container through Magick, and `StandardBaseLoader` rejects RAW directly so a router
change cannot bypass the policy. Consequently `ThumbnailCacheService`'s source-mtime
validity and `RenderSettingsHash` need no decoder-identity field.

### 2.6 X-Trans decode repeatability (2026-08-17)

LibRaw's X-Trans (Markesteijn) demosaic is not bit-reproducible across fresh processes
when OpenMP threading is uncontrolled; Bayer sources are. Production decode deliberately
does not pin OpenMP — serializing Markesteijn would cost real preview latency to remove
a one-sample difference (consequences in TESTING.md §3).

## 3. `StandardBaseLoader` (Magick.NET)

1. JPEG sources are pinged for native geometry before decoding. Preview loading uses the
   `jpeg:size` hint at `LargePreviewMaxDimension` only when the native long edge exceeds
   that hint, then derives both preview classes (preserves quality through DCT-scaled
   decode without upscaling smaller JPEGs).
2. `AutoOrient()` makes the pixels upright and the applied orientation is recorded.
3. For preview JPEG and HEIC only, capture a packed per-channel source-saturation mask
   from these upright encoded samples before any ICC/EOTF normalization. The inclusive
   ratio is `sample / encodedMaximum >= 253 / 255`; the reported encoded depth supplies
   the maximum (8-bit boundary 253/255, 10-bit boundary 1015/1023). If depth is not
   reported, the equivalent Q16 ratio is used. TIFF, PNG, and all other formats omit
   the artifact in v1. Full/export bases skip capture.
4. **Color normalization:**
   - Embedded ICC present → record its description, then transform from it to the
     gamma-1.0 Rec.2020 target defined in WORKING_SPACE.md.
   - No profile + CMYK colorspace → assume `ColorProfiles.USWebCoatedSWOP` as the
     deterministic source profile and transform to linear Rec.2020.
   - No profile otherwise → assume sRGB (industry default), apply the sRGB EOTF, then
     the exact sRGB→Rec.2020 matrix from WORKING_SPACE.md §2. The bitmap-backed
     edited-thumbnail proxy, whose upstream profile has already been discarded, uses
     the same direct path. This avoids lcms profile setup without changing the math.
   - Record `HadIccProfile` and the profile description, then strip **all** profiles
     after color conversion. Bases never retain ICC, EXIF/GPS, XMP, or thumbnails.
5. The target ICC has linear TRCs, and the direct sRGB path explicitly applies its EOTF,
   so normalized samples are already linear. Retag them as `ColorSpace.RGB` without a
   second transfer conversion, then ensure `Depth = 16`.
6. Preview pair: from the single color-normalized decoded buffer, independently resize
   interactive to 1600 and large to at most 3200. The interactive-size source-saturation
   mask is returned in `PreviewSourceAnalysis` beside the pair because large-base
   renders do not compute stats or masks. JPEG's existing 3200 DCT size hint is
   stable across viewport changes, so repeated resizes do not change decode identity.
7. `AsShotKelvin = 6504, AsShotTint = 0` (D65 anchor), `CamMul = null`;
   `FullWidth/FullHeight` = the original decoded dimensions after orientation
   (captured before the preview resize in step 5).

GIF decoding uses the first frame only. HEIC follows the identical standard path
through Magick.NET's HEIC coder backed by the bundled libheif for each target RID —
never Windows HEIF Image Extensions. HEIC tests gate first on
`MagickFormatInfo.Create(MagickFormat.Heic)?.SupportsReading`, then on an actual
fixture decode so delegate/package gaps are explicit skips. Source-saturation capture
uses the depth reported by that decode; the committed fixture reports 8-bit, while the
10-bit boundary is independently pinned at 1015/1023.

## 4. Ownership and concurrency

- `PreviewBaseCoordinator` owns the current preview pair and immutable source analysis
  with separable leases. Base identity is **(normalized file path,
  `BaseDecodeSettings.CacheKey`, preview-pair class)**; viewport dimensions are not a
  decode key. Each interactive lease exposes pixels and analysis installed by the same
  generation; neither artifact is published through a side channel.
- **Single-flight, newest-wins decodes:** at most one decode in flight per identity;
  a newer request (image switch, decode-settings change) cancels/supersedes it and
  stale results are disposed.
- A selected profile is resolved from one immutable, availability-gated snapshot before
  exact cache matching: its request token coordinates the generation, its resolved
  source/hash/status token identifies the result, and stale bases cannot promote render
  artifacts under a newer outcome token.
- **Only `BaseDecodeSettings` changes re-decode.** While a replacement decode is in
  flight, preview renders lease the held old base; the newest settings accumulate and
  completion emits one refresh using that latest state rather than a render backlog.
- A same-image Library/Develop round-trip retains the one current pair. Selection/path
  change, live-availability invalidation, folder replacement, decode-identity change,
  and shutdown retire it. A same-file decode-settings change retains the old interactive
  base for stale paint but retires the old large base immediately; its only normal lease
  is cancellable resting work.
- `RenderPipeline` never mutates the held base (OVERVIEW invariant 8); it clones
  internally. A superseded base is disposed only after any in-progress render against
  it completes (generation check).
- Export always calls `LoadFullBase` fresh per image (no base persistence); the render
  runs once and writes all variants (OUTPUT.md §2). It re-resolves a selected profile;
  missing, unavailable, corrupt, and hash-mismatched selections export through built-in
  characterization with a typed per-image warning.

## 5. Disk caches

- **Thumbnail cache:** `assets/thumbs/` contains one largest-wins unedited
  embedded/source JPEG per catalog image and uses a capacity-256 bounded writer. Cache
  dimensions come from a bounded JPEG SOF-header read before pixel decode. Existing
  150px entries satisfy Small and Medium; cache misses generate 150px, 192px, or 512px
  for Small, Medium, or Large. An undersized entry paints as a placeholder while a safe
  source upgrade is queued.
- **Rendered-preview cache:** `PreviewCacheService` stores the *last rendered output*
  (8-bit JPEG q90, 1600px) plus a sidecar `<id>.meta` containing `settingsHash`.
  - `settingsHash` = SHA-256 of canonical-JSON `EditSettings` v3 + `RenderPipeline.Version`
    + `BaseImage.Version` + the installed profile outcome token.
  - Develop entry: if cached hash matches current settings → decode its one BGRA buffer
    into the bitmap, display histogram, waveform, and display-floor clipping, then
    publish them atomically while the base loads in the background. Hash mismatch →
    paint it as a bitmap-only stale placeholder while generation-correlated live profile
    resolution, base decode, and fresh render replace or confirm it (no flash of
    nothing). Cache paint itself never opens an embedded profile or hydrates a source.
  - Existing atomic-write, bounded-channel, drop-oldest, 2 s drain rules all carry over.
  - A settled Develop selection may warm one neighbor in the inferred travel direction.
    The capacity-one worker drops replacement requests while cancellation drains,
    skips nonlocal sources and existing matches, disposes its decoded pair after one
    1600px render, and temporarily retains only the encoded q90 entry until its queued
    disk write lands. Selection reads that entry through the same cache/outcome path;
    source timestamp, settings hash, identity, and a live local-availability check gate
    the speculative slot.
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
  the rendered JPEG cache supplies immediate paint while a base loads.

## 6. Error handling

Loader failures are logged through `ImageServiceHelpers`. Request-correlated preview
outcomes preserve source-unavailable, native-runtime rejection, and unsupported-file
causes through the coordinator to the ViewModel. Library marks thumbnail failures and
RAW files whose Develop decode failed; Develop keeps an actionable per-image message
even when a cached preview remains visible. Runtime rejection is one global degraded
state rather than a per-file mark.

## 7. Verification

- Decoding the same file twice (any mix of preview/full) yields identical bases modulo
  resolution (golden ΔE ≈ 0 for full-vs-full; documented tolerance half-vs-full).
- A P3-tagged and an sRGB-tagged encode of the same picture produce near-identical bases
  (ΔE tolerance, TESTING.md §4.3).
- No base pixel depends on image statistics (burst determinism test).
- HEIC preview no longer attempts LibRaw (assert via debug log capture).
