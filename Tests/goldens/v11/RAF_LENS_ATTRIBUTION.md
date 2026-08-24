# Fujifilm RAF lens-correction attribution

The RAF re-baseline pins the qualified 23/31/23 geometric-distortion table to
its native-frame radius semantics for the committed X30 fixture. The feature's
`BaseImage.Version` move remains 13 → 14; render math and
`RenderPipeline.Version` remain unchanged.

Re-baselined images (old → new, display-sRGB CIE76):

- `fujifilm-x30__identity.png`: mean ΔE 3.784, p99 24.666
- `fujifilm-x30__exposure-plus-2.png`: mean ΔE 3.041, p99 21.797
- `fujifilm-x30__wb-3000.png`: mean ΔE 3.838, p99 25.222

All other golden assets were regenerated in the same invocation and remained
byte-identical. CA and vignetting stay deferred, so they do not contribute to
these movers.
