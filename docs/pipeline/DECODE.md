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
    BaseImageLoadOutcome LoadPreviewBaseWithOutcome(
        ImageFile file, BaseDecodeSettings decode, CancellationToken ct); // preview pair
    BaseImage? LoadFullBase(ImageFile file, BaseDecodeSettings decode, CancellationToken ct);    // native resolution
}
```

`BaseDecodeSettings` (OVERVIEW.md §4) carries the decode-affecting subset of
`EditSettings` — highlight reconstruction, FBDD, and the selected camera profile.
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

The loader applies no S-curve, saturation boost, or 8-bit conversion. RAW picture
formation belongs to the AgX crossing (TONE_ENGINE.md); no base look is applied to
crossing-on sources.

Post-decode steps, in order:

1. LibRaw's AHD Bayer and Markesteijn X-Trans paths both end at the same linear,
   neutralized, normalized three-channel camera-RGB span. `CameraRgbCharacterization`
   composes camera→sRGB with the exact sRGB→Rec.2020 matrix in `double`, then writes
   through Magick's writable Q16 cache pointer: camera `ushort` → matrix → one rounded
   Q16 write, with the only clamp at that write. The uncharacterized outcome imports
   the native codes unchanged. The destination is tagged `ColorSpace.RGB` before any
   sample is stored, avoiding Magick transfer conversion. Once LibRaw has produced its
   independently owned image, the loader recycles the context before import so raw and
   processing buffers do not overlap the render allocation. There is no PPM,
   intermediate full-frame pass, managed full-image copy, or second Magick pixel cache.
   When a DCP resolved successfully and WB facts are valid, its balanced-seam
   ForwardMatrix or ColorMatrix characterization replaces the built-in matrix in this
   same fused import. CameraCalibration, AnalogBalance, and the applicable matrix pair
   are interpolated at the as-shot CCT. Missing-WB and all typed profile rejections use
   the unchanged built-in transform. The matching HueSat payload and outcome token are
   attached to `BaseImageInfo` at the same installation boundary. Four-channel
   processed output is rejected as unsupported rather than truncated.
   The binding defaults OpenMP to at most sixteen workers unless the process already
   defines `OMP_NUM_THREADS`. This bounds the per-worker scratch space used by
   full-resolution X-Trans processing without changing decode precision or pixels.
2. LibRaw sometimes pre-rotates. The loader detects that through the dimension swap,
   applies EXIF orientation otherwise, and records `ExifOrientationApplied`.
3. Preview uses one LibRaw half-size decode. Two bases derive independently from that
   decoded buffer: interactive is the same one-step linear resize to 1600 as before;
   large is one resize to min(half-size result, 3200). Small sensors are never upscaled
   and preview never forces a full decode. Full bases remain native resolution.
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

Camera facts are copied immediately after `Unpack`, before the camera-native output
configuration is applied. `CamToSrgb` therefore remains camera→linear-sRGB and is not a
camera→working-space fact. `RawWorkingSpaceTests` pins the semantics against the
separately exposed camera-from-XYZ fact: row-normalize
`camera_from_xyz · (sRGB→XYZ)`, then invert it to reproduce `camera_to_srgb`.

At the same seam, after camera-fact copying and before `ConfigureOutput`/`Process`,
`RawSensorFrame` combines the typed bridge sensor identity with a zero-copy
`BorrowMosaic` lease. The frame owns that lease and always releases it before
`Process`; a held lease intentionally makes native process/recycle calls reject.
`RawSensorHistogram` then scans the visible photosites once on the existing decode
worker and token. Cancellation is checked every 256 visible rows and immediately
before processing. Cancellation escapes the loader; any other sampling/access fault is
logged once and leaves a valid decoded base with a null RAW fact.

Only integer CFA mosaics described by Bayer `filters > 1000` or the 36-byte X-Trans
table (`filters == 9`) qualify. No-CFA (`filters == 0`), Leaf tables (`filters == 1`),
other filter tables, invalid geometry/levels, and bridge-unavailable mosaics return no
RAW histogram. There is no Sdcb layout reader, second decode, source reread, or
processed-RGB substitute.

### 2.1 RAW Library previews

Library thumbnail extraction uses LibRaw's already-open context to return both the
encoded embedded preview and `ctx.Width`/`ctx.Height`, the visible dimensions rendered
by Develop. Camera-wide aspect differences are treated as preview padding: differences
at or below 3% pass through, while larger differences center-crop the embedded preview
toward the visible RAW aspect before the requested generation-size resize. Missing or
non-positive visible dimensions disable normalization but do not reject successfully
decoded preview bytes.
A valid LibRaw preview is never rejected on geometry grounds. If it remains undersized,
Library may try metadata-only EXIF extraction and a byte-level embedded-JPEG scan, but
it never opens the RAW container through Magick.

This policy is specific to LibRaw. EXIF thumbnails continue to reject missing geometry
and mismatches above 3%. Embedded-JPEG candidates remain unnormalized. Extraction
retains the largest safe candidate seen, continues while it is below the generation
target, and never starts a full RAW demosaic merely to satisfy a larger Library request.

### 2.2 Raw exposure

RAW decode leaves the linear pixels bias-free while recording a default-brightness
estimate in `BaseImageInfo.SourceExposureBiasEv`. The loader reads LibRaw's selected
embedded thumbnail from the already-open context, normalizes it to display sRGB, and
compares both images on a 48px-long-edge linear sampling grid. If the
preview and base aspect ratios differ by more than 2%, the base is center-cropped to
the preview ratio before comparison. This is deliberately the opposite crop direction
from Library normalization, which crops the embedded preview toward the visible RAW
frame. A bounded solver then finds the scalar EV whose neutral AgX render matches the
preview median. Base samples pass through the same default inset → log2/sigmoid → outset
crossing as the renderer, including the Rec.2020-to-sRGB comparison basis.

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
inside the tone-engine gain. The estimator and engine therefore share the anchored
post-gain quantity `a = v·2^(EVuser+EVsource)`; an unusable preview falls back to a
defensible decoded fact and never changes slider semantics. This re-derivation is the
reason base-image version 8 was introduced.

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

All supported platforms use `HappyPhoton.LibRaw.Native` 0.22.2.11 through the phase-2
binding. NuGet selects the matching RID assets. The binding resolves the bridge and its
LibRaw 0.22.2 companion from one package-local directory by absolute path; it never
allows a system or PATH copy to satisfy either name. Single-file extraction uses the
runtime's native search-directory contract, while loose development builds use their
RID-resolved output directory. `LibRawNativeSupport` performs one process-wide health
probe. It requires bridge ABI 3, numeric LibRaw version `0x001602` exactly, and
LibRaw's JPEG and zlib capability bits. An ABI mismatch stops before the versioned
runtime structure is queried. Resolution and load failures retain
bridge-versus-companion attribution, and every rejection records the safely observed
ABI, version, version string, and capability mask. One error-level diagnostic is
emitted for a rejected runtime. RAW decode and LibRaw preview/metadata extraction stay
unavailable until the installation is repaired; the About surface reports this degraded
state and includes the same facts in copied support text. Header-only RAW `Ping`, EXIF
thumbnail extraction, orientation reads, and decoding already-extracted preview bytes
remain permitted because they do not decode the RAW raster.

Bridge ABI 3 exposes a mutable mosaic lease whose writes are consumed by the following
LibRaw process call. Its output configuration also carries optional `user_sat`, named
`user_qual` requests, and an accept-only-verbatim full-resolution crop box. These are
interop capabilities only: `RawBaseLoader` does not use them until a pipeline consumer
defines the corresponding decode contract. An absent quality or crop restores LibRaw's
own sentinel, so the existing zero-initialized configuration remains pixel-identical.

The same loader parameters and golden fixtures cover Windows, Linux, and macOS. The
cross-platform comparison uses the mean ΔE bound documented in TESTING.md §3.

### 2.5 Single RAW decoder decision (2026-08-16)

This policy deliberately supersedes `LIBRAW_222.md` step 5's approved Magick fallback.
Inspection showed that Magick.NET's RAW support is itself LibRaw: its delegates include
`raw`, and RAF, CR2, CR3, NEF, DNG, ARW, and ORF report the LibRaw-backed `Dng` module.
`Magick.Native-Q16-x64.dll` from Magick.NET 14.15.0 embeds
`0.22.1-Release`, strictly older than Happy Photon's audited 0.22.2 runtime. It cannot
normally rescue a file rejected by 0.22.2; the accepted residual risk is a hypothetical
0.22.2 regression for which 0.22.1 happened to work.

On the X30 RAF fixture, full decode measured 8.1 seconds through Magick versus 1.9
seconds through the Happy Photon binding (about four times slower). At 100% the results
had near-identical detail with a small tone shift, consistent with two builds of the
same decoder and enough to make their pixels non-interchangeable in shared caches. The
Magick-carried build was also unaudited, unversioned in this repository, and invisible
to the native health gate. The former Windows-only RAF no-fallback carve-out is removed
as redundant: its original rationale was never recorded or reproducible, and the same
fixture currently decodes cleanly through Magick without a crash or corrupt output.

The invariant is enforced by construction: every RAW raster producer is LibRaw 0.22.2.
The removed production routes are the router's standard-loader descent, the
`MagickNetRawService` substitution, `ThumbnailService`'s full-container decode,
`EmbeddedPreviewExtractor`'s preview-frame decode, `MetadataService`'s RAW full-decode
catch, and path-based RAW input to `ImageStatsService`. `StandardBaseLoader` also rejects
RAW directly, so a future router change cannot bypass the policy.
Consequently `ThumbnailCacheService`'s source-mtime validity and
`RenderSettingsHash` need no decoder-identity field. No `BaseImage.Version` or
`RenderPipeline.Version` changes.

### 2.6 X-Trans decode repeatability (2026-08-17)

LibRaw's X-Trans (Markesteijn) demosaic is not bit-reproducible across fresh processes
when OpenMP threading is uncontrolled; Bayer sources are. Production decode deliberately
does not pin OpenMP: serializing Markesteijn would cost real preview latency to remove a
one-sample difference. TESTING.md §3 records the measurement and its consequence for
cache-integrity and byte-comparison checks.

## 3. `StandardBaseLoader` (Magick.NET)

1. JPEG sources are pinged for native geometry before decoding. Preview loading uses the
   `jpeg:size` hint at `LargePreviewMaxDimension` only when the native long edge exceeds
   that hint, then derives both preview classes (preserves quality through DCT-scaled
   decode without upscaling smaller JPEGs).
2. `AutoOrient()` makes the pixels upright and the applied orientation is recorded.
3. **Color normalization:**
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
4. The target ICC has linear TRCs, and the direct sRGB path explicitly applies its EOTF,
   so normalized samples are already linear. Retag them as `ColorSpace.RGB` without a
   second transfer conversion, then ensure `Depth = 16`.
5. Preview pair: from the single color-normalized decoded buffer, independently resize
   interactive to 1600 and large to at most 3200. JPEG's existing 3200 DCT size hint is
   stable across viewport changes, so repeated resizes do not change decode identity.
6. `AsShotKelvin = 6504, AsShotTint = 0` (D65 anchor), `CamMul = null`;
   `FullWidth/FullHeight` = the original decoded dimensions after orientation
   (captured before the preview resize in step 5).

GIF decoding uses the first frame only. HEIC follows the identical standard path
through Magick.NET's HEIC coder backed by the libheif bundled in the package's native
binary for each target RID (win-x64, linux-x64, osx-arm64); it does not call Windows
HEIF Image Extensions directly. Gate HEIC tests first on
`MagickFormatInfo.Create(MagickFormat.Heic)?.SupportsReading`, then on an actual fixture
decode so delegate/package gaps are explicit skips. The base depth is whatever the
bundled codec yields; 10-bit sources may flatten to 8-bit before the Q16 promotion and
remain a documented limitation.

## 4. Ownership and concurrency

- `PreviewBaseCoordinator` owns the current preview pair with separable leases. Base
  identity is **(normalized file path, `BaseDecodeSettings.CacheKey`, preview-pair
  class)**; viewport dimensions are not a decode key.
- **Single-flight, newest-wins decodes:** at most one decode in flight per identity;
  a newer request (image switch, decode-settings change) cancels/supersedes it and
  stale results are disposed, mirroring the existing thumbnail-session pattern.
- A selected profile is resolved from one immutable, availability-gated snapshot before
  exact cache matching. Its request token coordinates the generation; its resolved
  source/hash/status token identifies the result. Profile reads hash and parse the same
  bytes, and stale bases cannot promote render artifacts under a newer outcome token.
- **Only `BaseDecodeSettings` changes re-decode.** While a replacement decode is in
  flight, preview renders lease the held old base. The newest settings accumulate, and
  completion emits one refresh using that latest state rather than a render backlog.
- A path change retires both old bases immediately, subject only to outstanding leases.
  A same-file decode-settings change retains the old interactive base for stale paint
  but retires the old large base immediately; its only normal lease is cancellable
  resting work.
- `RenderPipeline` never mutates the held base (OVERVIEW invariant 8); it clones
  internally. Disposal of a superseded base happens only after any in-progress render
  against it completes (generation check, as with today's late thumbnail results).
- Export always calls `LoadFullBase` fresh per image (no base persistence); the render
  then runs once and writes all variants (OUTPUT.md §2). It re-resolves a selected
  profile. Missing, unavailable, corrupt, and hash-mismatched selections export through
  built-in characterization with a typed per-image warning.

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
    + `BaseImage.Version` + the installed profile outcome token.
  - Develop entry: if cached hash matches current settings → paint instantly, decode base
    in background for subsequent edits. Hash mismatch → paint it anyway as a stale
    placeholder while generation-correlated live profile resolution, base decode, and
    fresh render replace or confirm it (no flash of nothing). Cache paint itself never
    opens an embedded profile or hydrates a source.
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
