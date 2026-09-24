# Decode: Sources to `BaseImage`

Every supported file becomes the same canonical `BaseImage`: linear light, Rec.2020
primaries, D65, Q16, upright, and free of an aesthetic look. That normalization is
what lets preview and export share one source-agnostic renderer. See OVERVIEW.md §4
for the runtime contracts.

## 1. Loader routing

`IBaseImageLoader` defines preview-pair and full-resolution loads.
`LoadPreviewBaseWithOutcome` preserves typed failures; the `LoadPreviewBase` extension
detaches its interactive image. See `Services/IBaseImageLoader.cs`.

`BaseDecodeSettings` (OVERVIEW.md §4) carries the decode-affecting subset of
`EditSettings` — highlight reconstruction, optics toggles, and the selected camera profile.
Non-raw loaders accept and record
it but ignore it (their `From(EditSettings)` projection is still stored on
`BaseImageInfo.Decode` so cache keys stay uniform). The defaults are highlight clip
with built-in characterization.

`BaseLoaderRouter` sends RAW only to `RawBaseLoader`; `StandardBaseLoader` rejects
RAW in both capability and load paths. A rejected native runtime or unsupported file
cannot fall back to Magick raster decode. HEIC/HEIF and other standard formats use
`StandardBaseLoader`; HEIC's internal source-kind name is `HeicPlatform`.

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
| `fbdd_noiserd` | **0 (Off)** | retained native ABI field; the app no longer exposes decode-time FBDD |
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

1. Bayer and X-Trans converge on neutralized camera RGB. `CameraRgbCharacterization`
   composes the exact camera→Rec.2020 matrix in double and writes Q16 once directly
   into Magick's cache. LibRaw is recycled before import; there is no full-frame managed
   copy or second cache. Active optics samples/gains camera planes before this write
   (OPTICS.md). Valid DCP matrix/HueSat facts install atomically; rejected profiles and
   missing WB retain built-in characterization (CHARACTERIZATION.md). Four-channel
   processed output is rejected rather than truncated.
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

Camera facts are copied after Unpack, before output configuration: CamToSrgb remains
camera→linear-sRGB. WORKING_SPACE.md §3 owns its independent matrix oracle.

Preview loads sample the unpacked mosaic once before processing, via a zero-copy
`RawSensorFrame` lease released before `Process`. `RawSensorHistogram` produces
both the RAW histogram and packed source-saturation artifact, using order-independent
integer sums and cancellation checks. Only supported integer Bayer/X-Trans layouts
qualify; sampling faults leave valid pixels with empty analysis, while cancellation
escapes. There is no reread or processed-RGB substitute. The matching pair and analysis
install atomically; full/export loads skip sampling. RENDER.md §7 owns the predicates.

### 2.1 RAW Browse previews

Browse thumbnail extraction uses LibRaw's already-open context to return both the
encoded embedded preview and `ctx.Width`/`ctx.Height`, the visible dimensions rendered
by Develop. Aspect differences at or below 3% pass through as preview padding; larger
differences center-crop the embedded preview toward the visible RAW aspect before the
generation-size resize. Missing or non-positive visible dimensions disable
normalization but never reject successfully decoded preview bytes. If the result stays
undersized, Browse may try metadata-only EXIF extraction and a byte-level
embedded-JPEG scan, but never opens the RAW container through Magick.

This policy is specific to LibRaw: EXIF thumbnails still reject missing geometry and
mismatches above 3%, and embedded-JPEG candidates remain unnormalized. Extraction
retains the largest safe candidate seen, continues while it is below the generation
target, and never starts a full RAW demosaic merely to satisfy a larger Browse request.

### 2.2 Raw exposure

RAW decode leaves the linear pixels bias-free while recording a default-brightness
estimate in `BaseImageInfo.SourceExposureBiasEv`. The loader reads LibRaw's selected
embedded thumbnail from the already-open context, normalizes it to display sRGB, and
compares both images on a 48px-long-edge linear sampling grid; if the preview and base
aspect ratios differ by more than 2%, the base is center-cropped to the preview ratio
first (deliberately the opposite crop direction from Browse normalization). A bounded
solver then finds the scalar EV whose neutral AgX render matches the preview median,
with base samples passing through the same default inset → log2/sigmoid → outset
crossing as the renderer, including the Rec.2020-to-sRGB comparison basis.

The loader uses bounded `PreviewExposureEstimator` results and falls back to Fuji
MakerNote bias, RAF dynamic-range mode, then zero when preview evidence is unusable.
Clamping keeps estimates continuous; the unanchored ±1 EV limit prevents a high-key
camera preview from becoming a large global mid-tone lift. Preview/full demosaics are
approximate, so their estimates may differ within tested bounds. The renderer adds this
source fact to user Exposure in the tone-engine gain; estimation never changes slider
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

Bridge ABI 4 exposes header-stage generic maker-note lens identity without raster
decode or maker-specific native parsing; OPTICS.md owns managed matching policy.
Mutable mosaic leases, `user_sat`, `user_qual`, and the full-resolution crop box are
unused interop capabilities, not application decode settings.

The same loader parameters and golden fixtures cover Windows, Linux, and macOS; the
cross-platform comparison uses the mean ΔE bound documented in TESTING.md §3.

### 2.5 Single RAW decoder decision

There is no Magick RAW fallback: Magick.NET's RAW support is itself LibRaw — an older,
slower, unaudited build invisible to the native health gate, whose pixels are not
interchangeable with the audited 0.22.2 runtime in shared caches. Every RAW raster
producer is LibRaw 0.22.2, enforced by construction: no production route decodes a RAW
container through Magick, and `StandardBaseLoader` rejects RAW directly so a router
change cannot bypass the policy. Consequently `ThumbnailCacheService`'s source-mtime
validity and `RenderSettingsHash` need no decoder-identity field.

### 2.6 X-Trans decode repeatability

LibRaw's X-Trans (Markesteijn) demosaic is not bit-reproducible across fresh processes
when OpenMP threading is uncontrolled; Bayer sources are. Production decode deliberately
does not pin OpenMP — serializing Markesteijn would cost real preview latency to remove
a one-sample difference (consequences in TESTING.md §3).

## 3. `StandardBaseLoader` (Magick.NET)

Windows/Linux x64 use Magick.NET Q16 OpenMP 14.15.0; macOS keeps Q16 AnyCPU. Process
entry defaults `OMP_NUM_THREADS` and `MAGICK_THREAD_LIMIT` to at most sixteen workers to
bound X-Trans scratch space without changing decode precision or pixel math; explicit
nonblank values win. Native operations share that budget; the resting two-worker cap
applies only to managed kernels (repeatability: §2.6).

1. JPEG preview uses a native-geometry ping and a large-preview `jpeg:size` hint only
   when the source exceeds that size, preserving DCT-scaled decode without upscaling.
2. Auto-orient and record orientation. Preview JPEG/HEIC capture source saturation from
   upright encoded samples before normalization; other formats and full loads omit it.
   RENDER.md §7 owns the predicate and depth scaling.
3. Normalize profiled sources through the linear-Rec.2020 ICC target. Unprofiled CMYK
   uses USWebCoatedSWOP; other unprofiled sources and sRGB thumbnail proxies use the
   equivalent EOTF/matrix path (WORKING_SPACE.md §4). The direct kernel uses Magick's
   Q16 transfer samples then a double matrix and one clamp/round-half-up write.
4. Record the source profile description, strip all profiles, and tag the already-linear
   pixels RGB/Q16 without a second transfer. Independently derive interactive and large
   bases from this buffer; viewport changes never alter the JPEG decode hint.
5. Record original upright dimensions before resizing, D65 as-shot 6504/0, and no camera
   multipliers. Preview analysis travels beside the pair, never on `BaseImage`.

GIF decoding uses the first frame only. HEIC follows the identical standard path
through Magick.NET's HEIC coder backed by the bundled libheif for each target RID —
never Windows HEIF Image Extensions. HEIC tests gate first on
`MagickFormatInfo.Create(MagickFormat.Heic)?.SupportsReading`, then on an actual
fixture decode so delegate/package gaps are explicit skips. Source-saturation capture
uses the depth reported by that decode; the committed fixture reports 8-bit, while the
10-bit boundary is independently pinned at 1015/1023.

## 4. Ownership and concurrency

`PreviewBaseCoordinator` owns one current pair and immutable analysis through
separable leases. Identity is normalized file path, decode CacheKey and size class,
never viewport dimensions. Single-flight newest-wins replacement disposes stale work.
Selected profiles resolve from one availability-gated snapshot before exact matching;
request generations and resolved source/hash/status tokens cannot be interchanged.

Only decode settings re-decode. During replacement, interactive renders may lease the
old base while latest edits accumulate; completion refreshes once with that state. A
same-image Browse/Develop round trip retains the pair. Selection/path, availability,
folder, decode-identity and shutdown invalidate it. Decode-setting changes retire the
old large base immediately but retain interactive stale paint until replacement. All
disposal waits for outstanding leases; render never mutates a held base. Replacement
decodes await deferred retirement, bounding cleared pairs without blocking navigation.

Export loads a fresh full base per image, without preview analysis or persistence,
and renders once for its variants (OUTPUT.md §2). Profiles are re-resolved and typed
rejections use built-in characterization with a warning. NR is render-only; obsolete
stored `detail.noiseReduction` input is ignored.

## 5. Disk caches

- **Thumbnail cache:** `assets/thumbs/` contains one largest-wins unedited
  embedded/source JPEG per catalog image and uses a capacity-256 bounded writer. Cache
  dimensions come from a bounded JPEG SOF-header read before pixel decode. Existing
  150px entries satisfy Small and Medium; cache misses generate 150px, 192px, or 512px
  for Small, Medium, or Large. An undersized entry paints as a placeholder while a safe
  source upgrade is queued.
- **Rendered-preview cache:** `PreviewCacheService` stores the *last rendered output*
  (8-bit JPEG q90, 1600px) plus a sidecar `<id>.meta` containing `settingsHash` and
  the render's original-image and post-geometry view dimensions. Legacy hash-only
  sidecars remain readable but are rewritten with dimensions after the next render.
  - `RenderSettingsHash` hashes a JSON envelope of render version, base version, and
    canonical v4 settings, followed by a DCP outcome token suffix when present.
  - Develop entry: if cached hash matches current settings → decode its one BGRA buffer
    into the bitmap, display histogram, waveform, and display-floor clipping, then
    publish them atomically while the base loads in the background. Hash mismatch →
    paint it as a bitmap-only stale placeholder while generation-correlated live profile
    resolution, base decode, and fresh render replace or confirm it (no flash of
    nothing). Cache paint itself never opens an embedded profile or hydrates a source.
  - Existing atomic-write, bounded-channel, drop-oldest, 2 s drain rules all carry over.
  - Adjacent warming follows OVERVIEW.md invariant 10 and the guarded outcome contract
    in RENDER.md §11.
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
causes through the coordinator to the ViewModel. Browse marks thumbnail failures and
RAW files whose Develop decode failed; Develop keeps an actionable per-image message
even when a cached preview remains visible. Runtime rejection is one global degraded
state rather than a per-file mark.

## 7. Verification

- Decoding the same file twice (any mix of preview/full) yields identical bases modulo
  resolution (golden ΔE ≈ 0 for full-vs-full; documented tolerance half-vs-full).
- A P3-tagged and an sRGB-tagged encode of the same picture produce near-identical bases
  (ΔE tolerance, TESTING.md §3).
- No base pixel depends on image statistics (burst determinism test).
- HEIC uses the bundled Magick codec and never routes through LibRaw.
