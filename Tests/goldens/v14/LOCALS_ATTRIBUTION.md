# Render v14 Locals attribution

Render v14 adds optional linear local Exposure and settings v4. No inherited golden
contains locals, so the bypass must preserve v13 bytes. Regenerated using
HAPPY_PHOTON_UPDATE_GOLDENS=1 with GoldenRenderTests and GeometryGoldenTests.

The only permitted differences are the documented X30 native demosaic variance.

| Image | Byte identical | Mean ΔE76 | p99 ΔE76 |
|---|---|---:|---:|
| `adobe-rgb-reference__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `adobe-rgb-reference__identity.png` | True | 0.000000 | 0.000000 |
| `adobe-rgb-reference__wb-3000.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__chroma-combined.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__chroma-nr-50.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__color-mixer.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__contrast-plus-50.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__exposure-minus-2.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__full-combo-tonal.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__highlights-minus-100.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__identity.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__saturation-minus-50.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__shadows-plus-80.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__vibrance-minus-100.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__wb-3000.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__wb-9000-tint-minus-50.png` | True | 0.000000 | 0.000000 |
| `canon-eos-350d__wb-9000-tint-plus-50.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__chroma-combined.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__chroma-nr-50.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__color-mixer.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__contrast-plus-50.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__exposure-minus-2.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__full-combo-tonal.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__highlights-minus-100.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__identity.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__saturation-minus-50.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__shadows-plus-80.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__vibrance-minus-100.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__wb-3000.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__wb-9000-tint-minus-50.png` | True | 0.000000 | 0.000000 |
| `display-p3-reference__wb-9000-tint-plus-50.png` | True | 0.000000 | 0.000000 |
| `fujifilm-x30__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `fujifilm-x30__identity.png` | False | 0.000004 | 0.000000 |
| `fujifilm-x30__wb-3000.png` | True | 0.000000 | 0.000000 |
| `geometry__aspect-minus100.png` | True | 0.000000 | 0.000000 |
| `geometry__aspect-minus50.png` | True | 0.000000 | 0.000000 |
| `geometry__aspect-plus100.png` | True | 0.000000 | 0.000000 |
| `geometry__aspect-plus50.png` | True | 0.000000 | 0.000000 |
| `geometry__distortion-minus100.png` | True | 0.000000 | 0.000000 |
| `geometry__distortion-minus50.png` | True | 0.000000 | 0.000000 |
| `geometry__distortion-plus100.png` | True | 0.000000 | 0.000000 |
| `geometry__distortion-plus50.png` | True | 0.000000 | 0.000000 |
| `geometry__horizontal-minus100.png` | True | 0.000000 | 0.000000 |
| `geometry__horizontal-minus50.png` | True | 0.000000 | 0.000000 |
| `geometry__horizontal-plus100.png` | True | 0.000000 | 0.000000 |
| `geometry__horizontal-plus50.png` | True | 0.000000 | 0.000000 |
| `geometry__vertical-minus100.png` | True | 0.000000 | 0.000000 |
| `geometry__vertical-minus50.png` | True | 0.000000 | 0.000000 |
| `geometry__vertical-plus100.png` | True | 0.000000 | 0.000000 |
| `geometry__vertical-plus50.png` | True | 0.000000 | 0.000000 |
| `nikon-d70__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `nikon-d70__identity.png` | True | 0.000000 | 0.000000 |
| `nikon-d70__wb-3000.png` | True | 0.000000 | 0.000000 |
| `pentax-k-r__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `pentax-k-r__identity.png` | True | 0.000000 | 0.000000 |
| `pentax-k-r__wb-3000.png` | True | 0.000000 | 0.000000 |
| `reference-16bit__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `reference-16bit__identity.png` | True | 0.000000 | 0.000000 |
| `reference-16bit__wb-3000.png` | True | 0.000000 | 0.000000 |
| `reference-heic__identity.png` | True | 0.000000 | 0.000000 |
| `srgb-reference__exposure-plus-2.png` | True | 0.000000 | 0.000000 |
| `srgb-reference__identity.png` | True | 0.000000 | 0.000000 |
| `srgb-reference__wb-3000.png` | True | 0.000000 | 0.000000 |

All 65 cases compared; v13 is superseded and pruned after this report.
