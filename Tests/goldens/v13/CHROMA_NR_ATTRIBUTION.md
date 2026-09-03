# Render v12 chroma-NR attribution

Render v12 replaces the trailing chroma box blur with five-scale wavelet
soft-thresholding inside the shared noise-reduction stage, before capture sharpen. The
finest band is full-resolution and deeper bands are progressively half-resolution.
Existing settings are not migrated; only renders with Chroma NR above zero use the
new pixels.

The 63 inherited v11 PNGs were regenerated from the same matrix. Sixty are
byte-identical. The three Fujifilm X30 files retain the documented native-demosaic
run-to-run variance:

| Golden | Mean ΔE76 | p99 ΔE76 |
|---|---:|---:|
| `fujifilm-x30__identity.png` | 0.000347 | 0.000000 |
| `fujifilm-x30__exposure-plus-2.png` | 0.000255 | 0.000000 |
| `fujifilm-x30__wb-3000.png` | 0.000335 | 0.000000 |

Two additive baselines exercise Chroma NR 50 on the reference Canon RAW and the
Display-P3 JPEG. The active generation therefore contains 65 PNGs; v11 is superseded
and pruned.

The turn-3 WYSIWYG correction restored every resolution-mapped chroma scale in
downsampled previews and optimized the finest-scale kernel and gamut reconstruction.
The full 65-image matrix was regenerated; every export PNG remained byte-identical.
`GoldenRenderTests` also passed the preview/export comparison with the restored preview
scales.
