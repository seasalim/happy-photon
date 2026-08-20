# Output: Display, Export, and Metadata

The output layer turns the display-referred Q16 render into either an Avalonia bitmap
or an encoded copy. It also reconstructs metadata deliberately because rendered pixels
can no longer inherit source profiles or orientation tags safely.

## 1. Display path

The preview `RenderResult.Image` (display-referred sRGB, Q16) passes through
`BitmapConversionService.ConvertToBitmap` to become an 8-bit BGRA Avalonia bitmap.
The 16-to-8-bit step deliberately uses native nearest-level quantization without
dithering. On the generated 4096-step gradient, it measured 0.2499 LSB mean absolute
error and 0.4981 LSB maximum error. Deterministic ordered 8×8 dithering reduced 8×8
block-mean error from 0.1190 to 0.0052 LSB, but increased point error to 0.3322 LSB
mean and 0.9883 LSB maximum and would add a full-image pass to interactive preview.
Happy Photon therefore keeps the lower-error, lower-cost conversion; JPEG/WebP also
receive lossy encoder noise, while PNG remains an explicit 8-bit output.

Nearest-level is the **only** 16-to-8 conversion in the product: display, PNG, JPEG,
and WebP all reach it through Magick's native quantizer, so preview and an sRGB export
agree code for code before any lossy encoding. A Display P3 export deliberately has
different channel codes. Never call `Depth = 8` on a render — it
quantizes toward zero and silently shifts roughly half of all samples one code below
the preview (measured 30–51% of channel samples, `pngMinusPreview` in {−1, 0}).
Request 8-bit output through the encoder's own define instead. The opt-in precision
harness (TESTING.md §5) gates this equality per fixture as `previewPng=pass`.
`RenderResult` can carry optional semantic clipping masks. Develop requests them only
while the clipping latch or a histogram-triangle peek is active; normal display and
all export renders remain mask-free (RENDER.md §7).

## 2. Export flow (`ImageExportService`)

Before pixel work, export snapshots `OutputColorSpace` once, so mutable dialog state
cannot produce P3 pixels with an sRGB tag or the reverse. Per image:
`LoadFullBase` → one unresized `RenderDisplayRec2020` → per variant, in descending size
order: sRGB-decode → progressive linear-light resize → sRGB-encode → optional output
sharpen → sRGB-decode → target convert → clamp → sRGB-encode → metadata apply (§4) →
encode (§3) → `ExportSafety` checks → write. The export loop transfers ownership of the
last progressive variant instead of cloning it.

Preview uses the same finalizer with output sharpening disabled and sRGB selected.
All geometry, tone, chroma, detail, resize, and sharpening work is target-independent;
only the trailing convert, clamp, encode, and profile differ between sRGB and P3.

Before desktop export starts, the dialog classifies every selected original and totals
the logical size of files that require hydration. If the count is nonzero, it shows
that exact scope and waits for **Download / Export** confirmation. Cancellation makes
no base-loader or source-metadata call. Only the confirmed image list receives
`UserApprovedHydration`; ordinary and agent exports retain background intent and cannot
silently download cloud-only originals. Provider cancellation is best effort after a
download has started.

## 3. Encoders

| Format | Rules |
|--------|-------|
| JPEG | quality = user setting (default 85). Chroma subsampling: `jpeg:sampling-factor` = `4:4:4` when quality ≥ 90, else `4:2:0` (explicit, no longer encoder default). Baseline (non-progressive). |
| PNG | 8-bit output through `PngWriteDefines.BitDepth = 8`; the Q16 working pipeline does not imply 16-bit PNG output. |
| WebP | quality = user setting, lossy. |

**Every export embeds the profile matching its rendered target** — `ColorProfile.SRGB` for
the default sRGB path, or the 480-byte Compact ICC Profiles `DisplayP3-v4.icc` (CC0) for
Display P3. JPEG, PNG, and WebP each preserve the selected profile. Display P3 shares the
sRGB transfer, so resize and tone encoding continue to use the existing transfer LUTs.

**Output sharpen** uses the exact Rec.2020 luma authority in RENDER.md §9 and is
governed by exactly one condition: the export dialog's
"Output sharpening" checkbox (default on). Checkbox on → applied after each resize to
a sized variant ≤ 2560px (luminance unsharp `sigma 0.5, amount 0.3, threshold 0.005`).
Checkbox off → never applied. Hi-res (unresized) variants never receive it. It is
fully independent of the fixed capture-sharpen stage.

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
3. **GPS:** kept by default; removed when the export dialog's **"Strip location data"**
   checkbox is set (persisted app setting, default off). Stripping removes the entire
   GPS IFD.
4. ICC: §3's selected output profile — never the source profile.
5. XMP/IPTC: not copied.

RAW and non-RAW sources use the same policy. Capture facts survive where available,
while private or structurally stale metadata is never carried through accidentally.

## 5. Agent (MCP) surface

`export_images` uses the same encoder and metadata policy as desktop export. Its optional
`outputColorSpace` is `srgb` by default and accepts `displayP3`; it never inherits mutable
desktop color-space state.
`apply_edit_settings` accepts the current v2 fields, including `wb`, `baseLook`, and
`hlReconstruction`; omitted fields leave current values unchanged. The privacy
boundary remains metadata and thumbnail-derived statistics only, and
`get_image_stats` independently requests `(150, 150)` and measures the unedited base
thumbnail. Before statistics are calculated, every cache input is resampled to a
canonical 150px long edge with Lanczos, whether the stored source thumbnail is a legacy
150px entry or a promoted 512px entry. This keeps statistics stable without making the
agent promote the Library cache as a side effect. Agent calls never receive hydration
approval: image summaries expose `sourceAvailability`, and operations that need an
online-only original return failure code `hydration_required`.

## 6. Verification

- Exported JPEG opened in a color-managed browser stays within the preview's colorimetric
  bounds. sRGB retains the golden ΔE/code gates; Display P3 is converted through its
  embedded profile before comparison, and the synthetic native-P3 fixture is the
  gamut-survival sentinel. Edited target agreement is governed by WORKING_SPACE.md §9.3.
- Preview BGRA and sRGB PNG read-back carry identical 8-bit codes for the same render
  (precision harness `previewPng` gate, §1).
- Export of a RAW carries capture date, camera, exposure EXIF; orientation displays
  upright everywhere; no embedded stale thumbnail (verify with exiftool in CI or a
  Magick profile read-back test).
- "Strip location data" removes all GPS tags; default keeps them (round-trip test with
  a GPS-tagged asset).
- Subsampling: quality 92 export shows `4:4:4`, quality 80 shows `4:2:0` (read back via
  Magick attributes).
- Export hydration tests inject source availability and assert the selected cloud count
  and logical bytes, zero background source calls, and one base/metadata read for each
  image in the approved set.
- Agent-statistics tests compare representative 150px and promoted-cache inputs after
  normalization, with luminance within 2 levels and relative sharpness within 35%.
