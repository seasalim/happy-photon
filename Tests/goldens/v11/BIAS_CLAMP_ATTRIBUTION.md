# Exposure-bias clamp attribution (fujifilm-x30)

`PreviewExposureEstimator.SelectBias` now clamps the measured preview estimate
into metadata ± 0.5 EV instead of discarding it when the disagreement exceeds
0.5 EV. Decodes of the same file measure a few hundredths of an EV apart
(the native demosaic is thread-nondeterministic), so the hard accept/reject
threshold flipped boundary files by the full disagreement between decodes —
the NR-toggle brightness flip on the user's X-T5 RAF (measured ~1.23 vs
MakerNote 1.72).

Only `fujifilm-x30.raf` sits in the former reject zone among golden assets:
MakerNote bias 0.58 EV, measured estimate ≥ 1.08 EV, selected bias moves
0.58 → 1.08 (the clamp ceiling). Visual check against the file's embedded
out-of-camera JPEG confirms the brighter render is the closer match.

Re-baselined images (old → new, display-sRGB CIE76):

- `fujifilm-x30__identity.png`: mean ΔE 5.676, p99 6.976
- `fujifilm-x30__exposure-plus-2.png`: mean ΔE 4.079, p99 6.364
- `fujifilm-x30__wb-3000.png`: mean ΔE 6.050, p99 7.335

All other assets are unchanged; no `RenderPipeline.Version` bump (the render
math is untouched — only the decoded source-bias fact moved). `BaseImage.Version`
bumps 9 → 10 so persisted previews and rendered thumbnails produced with the
pre-clamp bias cannot replay.
