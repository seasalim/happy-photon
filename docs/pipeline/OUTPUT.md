# Output: Display, Export, and Metadata

The output layer turns the display-referred Q16 render into either an Avalonia bitmap
or an encoded copy. It also reconstructs metadata deliberately because rendered pixels
can no longer inherit source profiles or orientation tags safely.

## 1. Display path

The preview `RenderResult.Image` (display-referred sRGB, Q16) passes through
`BitmapConversionService.ConvertToBitmap` to become an 8-bit BGRA Avalonia bitmap. That
bitmap remains the canonical sRGB object used by preview promotion, Compare,
Before/After, and the preview cache. On Windows, viewer surfaces synchronously derive a
separate 8-bit BGRA display copy at the view boundary when the window's monitor has a
supported non-sRGB matrix/TRC profile. The display leg is decode LUT → 3×3 matrix →
encode LUT; it never feeds the histogram, clipping mask, white-balance picker, cache,
promotion, or export. An absent or sRGB-shaped profile shows the canonical bitmap
directly with no allocation. LUT-based and MHC2 profiles are reported in About and treat
the monitor as sRGB; Windows Auto Color Management does the same to avoid a second
correction. Thumbnails and placeholders remain outside this app-managed display leg. On
macOS the app tags the window's CAMetalLayer as sRGB once it exists (after the first
frame) and the compositor color-matches the whole window, thumbnails and chrome
included; the display leg stays identity there and About reports "managed by macOS".
Native nearest-level quantization avoids a full-image dithering pass and minimizes point
error; lossy encoders add their own noise, while PNG remains explicit 8-bit output.

Nearest-level is the **only** 16-to-8 conversion in the product: display, PNG, JPEG, and
WebP all reach it through Magick's native quantizer, so preview and an sRGB export agree
code for code before any lossy encoding. TIFF keeps the Q16 values and performs no 8-bit
conversion. A Display P3 export deliberately has different channel codes. Never call
`Depth = 8` on a render — it quantizes toward zero and can shift samples one code below
the preview. Request 8-bit output through the encoder's own define instead; display/PNG
code equality has no automated test. `RenderResult` can carry optional semantic clipping
masks. Develop requests them only while the clipping latch or a histogram-triangle peek
is active; normal display and all export renders remain mask-free (RENDER.md §7).

## 2. Export flow (`ImageExportService`)

Before pixel work, export resolves an immutable job containing the captures, armed
output sizes, cloned edit settings, all output settings, and the target path for every
photo-size pair. Execution reads only that job, so later workspace changes cannot
alter pixels, metadata policy, encoding, or destinations. Outcomes are recorded per
target pair, while each capture still follows the shared flow below:
`LoadFullBase` → one unresized `RenderDisplayRec2020` → per variant, in descending size
order: sRGB-decode → progressive linear-light resize → sRGB-encode → optional output
sharpen → vignette → grain → sRGB-decode → target convert → clamp → sRGB-encode → optional text watermark → metadata apply (§4) →
encode (§3) → write-time authorization check → temporary encode → atomic install. A
target that existed during preflight is replaceable only after the grouped overwrite
confirmation; every other install uses create-new semantics, so a file appearing after
preflight is never overwritten. The export loop transfers ownership of the last
progressive variant instead of cloning it.

The optional single-line text watermark is snapshotted with output settings and drawn
last at each finished size, after target encoding. Export and Preview output share this
step; Develop, resting previews and thumbnails never draw it. SkiaSharp coverage over the
union of the layout box and measured ink bounds, which preserves italic overhangs, is
blended only inside that image-clamped rectangle into Q16 output-encoded RGB, preserving
alpha. Size and margin use the short edge; long text fits within both margins. A disabled
watermark is a null snapshot and skips the step; enabled blank text blocks export.

The job validates sizes and naming before execution; retry reuses the saved job.
User-facing selection, layout, naming examples and proof behavior belong to
[WORKFLOW.md](../WORKFLOW.md) §6. A proof identifies its canonical output encoding to
the display leg; conversion for a monitor never changes exported pixels or ICC tags. A
displayed proof suppresses display-fit resting refinement because sharpening is defined
at output dimensions.

All geometry, tone, chroma, detail, resize, sharpening, and effects work is
target-independent; only the trailing convert, clamp, encode, and profile differ
between sRGB and P3. Effects are snapshotted with the edit settings and run separately
at each variant's output dimensions, after that variant's resize and sharpen.

Before desktop export starts, the workspace classifies every selected original and totals
the logical size of files that require hydration. If the count is nonzero, it shows
that exact scope and waits for **Download / Export** confirmation. Cancellation makes
no base-loader or source-metadata call. Only the confirmed image list receives
`UserApprovedHydration`; unconfirmed export work retains background intent and cannot
silently download cloud-only originals. Provider cancellation is best effort after a
download has started.

## 3. Encoders

| Format | Rules |
|--------|-------|
| JPEG | quality = user setting (default 85). Chroma subsampling: `jpeg:sampling-factor` = `4:4:4` when quality ≥ 90, else `4:2:0` (explicit, no longer encoder default). Baseline (non-progressive). |
| PNG | 8-bit output through `PngWriteDefines.BitDepth = 8`; the Q16 working pipeline does not imply 16-bit PNG output. |
| WebP | quality = user setting, lossy. |
| TIFF | 16-bit RGB, Deflate/ZIP compression, no alpha. Depth is selected through encoder settings; `image.Depth` remains untouched. Normalized EXIF is carried into the TIFF IFD. |

**Every export embeds the profile matching its rendered target** — `ColorProfile.SRGB` for
the default sRGB path, or the 480-byte Compact ICC Profiles `DisplayP3-v4.icc` (CC0) for
Display P3. JPEG, PNG, WebP, and TIFF each preserve the selected profile. Display P3 shares the
sRGB transfer, so resize and tone encoding continue to use the existing transfer LUTs.

**Output sharpen** uses the exact Rec.2020 luma authority in TONE_ENGINE.md §6. **Off**
never applies it. **Screen** is the compatibility default and applies after an actual
resize to a sized variant ≤ 2560px (luminance unsharp `sigma 0.5, amount 0.3,
threshold 0.005`); unresized output is unchanged. **Print** also applies to full-size
output and scales stronger sigma/amount constants across ≤1600px, ≤3200px, and larger
long-edge regimes. It remains fully independent of the fixed capture-sharpen stage.

## 4. Metadata policy (`ExportMetadataService`)

Renders are rebuilt pixels; profiles do not survive the pipeline. This service
deliberately reconstructs metadata on the encoded output:

1. **EXIF copy:** after the same live availability/intent check as base decode, read
   the original file's EXIF (Magick `Ping` + `GetExifProfile()`). Background work does
   not call `Ping` for a source that requires hydration. A confirmed desktop export may.
   Works for JPEG/TIFF/most raws; when unavailable (some raws/platforms), synthesize a
   minimal EXIF from the catalog's `RawMetadata`/`ImageMetadata` DTO: Make, Model,
   DateTimeOriginal, ISO, FNumber, ExposureTime, FocalLength, LensModel.
2. **Fixups on the copied profile:**
   - Orientation tag → TopLeft (pixels are upright).
   - Remove the embedded EXIF thumbnail (stale after edits).
   - Remove `PixelXDimension`/`PixelYDimension` (or set to actual output size).
   - Set `Software = "Happy Photon <version>"`.
3. **GPS:** kept by default; removed when the Export workspace's **"Remove location data"**
   checkbox is set (persisted app setting, default off). Stripping removes the entire
   GPS IFD.
4. ICC: §3's selected output profile — never the source profile.
5. XMP/IPTC: not copied.

RAW and non-RAW sources use the same policy. Capture facts survive where available,
while private or structurally stale metadata is never carried through accidentally.

## 5. Verification

- Exported JPEG opened in a color-managed browser stays within the preview's colorimetric
  bounds. sRGB retains the golden ΔE/code gates; Display P3 is converted through its
  embedded profile before comparison, and the synthetic native-P3 fixture is the
  gamut-survival sentinel. Edited target agreement is governed by WORKING_SPACE.md §9.3.
- Preview BGRA and sRGB PNG read-back carry identical 8-bit codes for the same render
  (§1); this is a contract, currently without automated enforcement.
- Export of a RAW carries capture date, camera, exposure EXIF; orientation displays
  upright everywhere; no embedded stale thumbnail (verify with exiftool in CI or a
  Magick profile read-back test).
- "Remove location data" removes all GPS tags; default keeps them (round-trip test with
  a GPS-tagged asset).
- Subsampling: quality 92 export shows `4:4:4`, quality 80 shows `4:2:0` (read back via
  Magick attributes).
- Export hydration tests inject source availability and assert the selected cloud count
  and logical bytes, zero background source calls, and one base/metadata read for each
  image in the approved set.
- TIFF read-back is Q16 bit-exact for both output color spaces and pins 16 bits/sample,
  ZIP compression, RGB-only channels, exact ICC bytes, and normalized EXIF fields.
