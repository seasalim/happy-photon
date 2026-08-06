# Output: Display, Export, and Metadata

The output layer turns the display-referred Q16 render into either an Avalonia bitmap
or an encoded copy. It also reconstructs metadata deliberately because rendered pixels
can no longer inherit source profiles or orientation tags safely.

## 1. Display path

`RenderResult.Image` (display-referred sRGB, Q16) passes through
`BitmapConversionService.ConvertToBitmap` to become an 8-bit BGRA Avalonia bitmap.
The 16-to-8-bit step deliberately uses native nearest-level quantization without
dithering. On the generated 4096-step gradient, it measured 0.2499 LSB mean absolute
error and 0.4981 LSB maximum error. Deterministic ordered 8×8 dithering reduced 8×8
block-mean error from 0.1190 to 0.0052 LSB, but increased point error to 0.3322 LSB
mean and 0.9883 LSB maximum and would add a full-image pass to interactive preview.
Happy Photon therefore keeps the lower-error, lower-cost conversion; JPEG/WebP also
receive lossy encoder noise, while PNG remains an explicit 8-bit output.
`RenderResult` can carry optional clipping masks, but the current display path does
not request them (RENDER.md §7).

## 2. Export flow (`ImageExportService`)

Per image: `LoadFullBase` → `RenderPipeline.Render(intent: Export)` **once** → per
variant: resize (descending sizes with progressive downscaling; resizes run in
linear light per RENDER.md §1.1, same filter as the preview path) → output sharpen →
metadata apply (§4) → encode (§3) → `ExportSafety` checks → write.

The render math is identical to the preview path; only `MaxDimension` and base
resolution differ. WYSIWYG tests measure the remaining resize/decode difference.

## 3. Encoders

| Format | Rules |
|--------|-------|
| JPEG | quality = user setting (default 85). Chroma subsampling: `jpeg:sampling-factor` = `4:4:4` when quality ≥ 90, else `4:2:0` (explicit, no longer encoder default). Baseline (non-progressive). |
| PNG | 8-bit output (`Depth = 8` before write); the Q16 working pipeline does not imply 16-bit PNG output. |
| WebP | quality = user setting, lossy. |

**Every export embeds the sRGB ICC profile** (`ColorProfile.SRGB`) — for all formats
that support it, all source kinds.

**Output sharpen** is governed by exactly one condition: the export dialog's
"Output sharpening" checkbox (default on). Checkbox on → applied after each resize to
a sized variant ≤ 2560px (luminance unsharp `sigma 0.5, amount 0.3, threshold 0.005`).
Checkbox off → never applied. Hi-res (unresized) variants never receive it. It is
fully independent of the fixed capture-sharpen stage.

## 4. Metadata policy (`ExportMetadataService`)

Renders are rebuilt pixels; profiles do not survive the pipeline. This service
deliberately reconstructs metadata on the encoded output:

1. **EXIF copy:** read the original file's EXIF (Magick `Ping` + `GetExifProfile()`).
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
4. ICC: §3's sRGB profile — never the source profile (pixels are sRGB now).
5. XMP/IPTC: not copied.

RAW and non-RAW sources use the same policy. Capture facts survive where available,
while private or structurally stale metadata is never carried through accidentally.

## 5. Agent (MCP) surface

`export_images` uses the same encoder and metadata policy as desktop export.
`apply_edit_settings` accepts the current v2 fields, including `wb`, `baseLook`, and
`hlReconstruction`; omitted fields leave current values unchanged. The privacy
boundary remains metadata and thumbnail-derived statistics only, and
`get_image_stats` measures the unedited base thumbnail.

## 6. Verification

- Exported JPEG opened in a color-managed browser matches the in-app preview (golden
  ΔE bound; the P3-source test asset is the sentinel case).
- Export of a RAW carries capture date, camera, exposure EXIF; orientation displays
  upright everywhere; no embedded stale thumbnail (verify with exiftool in CI or a
  Magick profile read-back test).
- "Strip location data" removes all GPS tags; default keeps them (round-trip test with
  a GPS-tagged asset).
- Subsampling: quality 92 export shows `4:4:4`, quality 80 shows `4:2:0` (read back via
  Magick attributes).
