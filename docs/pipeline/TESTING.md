# Pipeline Spec — Testing: Goldens, Assets, Tolerances

The rework is only safe because of this harness. WP0.1 builds it; every later work
package extends it. Unit tests follow existing conventions (`Tests/*Tests.cs`, xUnit,
`dotnet test HappyPhoton.sln`).

## 1. Sample assets (`Tests/assets/`)

Committed directly (no LFS), total budget **≤ 81 MiB, ≤ 30 MiB per file**. The budget
covers five distinct raws plus the byte-identical burst copy; prefer the
oldest/smallest CC0 body per mosaic type. Provenance is recorded in
`Tests/assets/README.md` with per-file source URL + license.

| Asset | Purpose | Source |
|-------|---------|--------|
| Small Bayer raw ×2 (e.g. Canon CR2 + Nikon NEF, ≤ 15 MB ea. — prefer small-sensor bodies) | decode, WB, goldens | raw.pixls.us, CC0 only |
| X-Trans RAF | Fuji path | raw.pixls.us CC0 |
| DNG | Adobe container path | raw.pixls.us CC0 |
| High-ISO Bayer raw | FBDD quality/runtime evaluation | CC0 research dataset |
| sRGB JPEG with EXIF+GPS+orientation 6 | metadata policy, orientation | author with exiftool from a CC0 photo |
| Display-P3 JPEG of the same picture as an sRGB JPEG | ICC normalize sentinel | generate via Magick from a CC0 source |
| AdobeRGB JPEG | second ICC case | generate |
| 16-bit TIFF | depth preservation | generate |
| HEIC | platform codec path (skip test when codec absent) | generate/CC0 |
| Synthetic gradient PNG (0→1 ramp, generated in-test) | LUT banding, monotonicity | code |

Raw-file burst pair: two consecutive CC0 frames of a similar scene if obtainable; else
duplicate one raw byte-for-byte under two names (sufficient for determinism testing).

## 2. Golden mechanism

- Goldens are rendered PNGs stored under `Tests/goldens/v<RenderPipeline.Version>/`,
  named `<asset>__<settings-case>.png`, rendered at **long edge 500** (keeps the golden
  directory ≤ 25 MB without LFS).
- **Active baseline marker:** `Tests/goldens/ACTIVE_VERSION` (plain text: `v0`, `pending`,
  `v1`, …). Golden and WYSIWYG suites read it; the literal value `pending` makes them
  report **skipped-with-reason** ("awaiting re-baseline") instead of failing. This is
  how the integration branch stays green mid-rework (roadmap: integration strategy).
- Comparison: per-pixel CIE76 ΔE on sRGB→Lab conversion. Report mean and p99.
- Re-baselining: `HAPPY_PHOTON_UPDATE_GOLDENS=1 dotnet test` regenerates; CI never sets
  it. A golden diff in review must be justified by a `RenderPipeline.Version` bump or a
  spec change in the PR.
- Settings cases, phased with the pipeline (10 total):
  - **Tonal set** (baselined as v1 at WP2.5, when the chromatic stage is still
    identity): identity; +2 EV; −2 EV; highlights −100; shadows +80; contrast +50;
    full-combo tonal preset — 7 cases.
  - **WB set** (added at WP3.2, which bumps `RenderPipeline.Version` → 2 and
    regenerates all baselines as v2): WB 3000 K; WB 9000 K tint +50; WB 9000 K
    tint −50 — 3 cases. Never baseline WB cases while the chromatic stage is a stub —
    that would golden "WB ignored" and immediately invalidate itself.
  - v2's tonal-case renders were required to be **byte-identical** to their v1
  counterparts when the v2 baselines were introduced (as-shot skips the chromatic
  stage exactly). Keep only the active baseline generation; prune obsolete versions
  whenever a new one is activated.

The full-combo tonal case is pinned to exposure +1 EV, brightness +10, contrast +25,
shadows +35, highlights −50, and a monotone curve through `(0,0)`, `(0.25,0.20)`,
`(0.75,0.82)`, `(1,1)`. Chroma, white balance, geometry, and preset identity remain
at defaults.

WP0.1's v0 baseline uses the v1 asset × tonal-case matrix below. It captures the
current export-quality path as a calibration reference: full decode, current edit
application, one resize to long edge 600, then color-profile normalization to sRGB
for the golden PNG. It does not use the half-size preview decode or preview JPEG cache.

### 2.1 Asset × case matrix (keeps golden count and runtime bounded)

| Asset | v1 cases (WP2.5) | added at v2 (WP3.2) |
|-------|------------------|---------------------|
| Reference Bayer raw (the CR2) | all 7 tonal | all 3 WB |
| Display-P3 JPEG | all 7 tonal | all 3 WB |
| NEF, RAF, DNG, AdobeRGB JPEG, sRGB JPEG, 16-bit TIFF | identity, +2 EV | WB 3000 K |
| HEIC | identity (skippable per §6) | — |

27 golden files at v1, 39 at v2. The clipped-highlight case uses the bright water
reflection in the reference CR2, as verified during WP4.1
([DECODE.md §2.2](DECODE.md#22-highlight-reconstruction-evaluation)).

## 3. Tolerances (normative)

| Comparison | Bound |
|------------|-------|
| Same base, repeated render, same platform | bit-identical |
| Golden vs current, same platform | mean ΔE ≤ 1.0, p99 ≤ 3.0 |
| Preview render vs export render downscaled to same size | mean ΔE ≤ 1.5, p99 ≤ 4.0 |
| Full-decode base vs half-decode base (raw, at common preview size up to 1600px) | mean ΔE ≤ 2.1 (documented gap) |
| P3-tagged vs sRGB-tagged same-picture bases | mean ΔE ≤ 1.5 |
| Cross-platform (M6): win/linux/mac renders of same case | mean ΔE ≤ 2.0 |

The RAW half/full bound pins the Canon EOS 350D fixture's measured LibRaw
half-size demosaic gap of mean ΔE 2.087 at the common preview size. A resize-filter
audit ranged from 2.086 to 2.205, confirming the residual is decode sampling rather
than a render or resize regression.

**OS gating policy at M6:** goldens are generated on Linux CI (canonical). Linux uses
the same-platform mean/p99 bounds above. Windows and Apple Silicon macOS compare every
RAW and non-RAW case to that canonical baseline with the cross-platform mean ΔE ≤ 2.0
bound. HEIC remains the only skippable golden when the platform codec reports no read
support; codec skips always carry an explicit reason.

## 4. Required suites

1. **`ToneLutTests`**: monotonicity property (`lut[i+1] ≥ lut[i]`, non-strict — clamp
   plateaus are legal) over randomized valid settings with **identity or monotone user
   curves** (seeded, ≥ 1000 draws; non-monotone user curves are allowed by the product
   and exempt from this property — RENDER.md §5.5); identity case reproduces `E(x)`
   within 1 LSB@8bit; shoulder C1 continuity numeric check; every §5 formula pinned at
   3 sample points, including the clamp boundaries (e.g. Brightness+Contrast at +100
   must plateau, not fold back).
2. **`WhiteBalanceModelTests`**: WHITE_BALANCE.md §9 list.
3. **`RenderDeterminismTests`**: repeated render bit-identical; burst pair identical;
   settings hash stable across process runs (canonical JSON ordering).
4. **`GoldenRenderTests`**: §2 matrix.
5. **`WysiwygTests`**: preview vs export bound (§3 row 3) for the settings cases.
6. **Current-format boundary tests**: `EditSettingsJsonTests` pins canonical ordering,
   clone-before-clamp behavior, range validation, removed WB modes, and rejection of
   every non-v2 write. `CatalogSchemaTests` pins the clean new schema, acceptance of
   harmless extra columns, and actionable startup rejection for missing columns;
   `CatalogPersistenceTests` pins neutral no-write recovery for null, malformed, or
   non-v2 rows. Preset and MCP tests require explicit/current versions and reject v1.
7. **`ExportMetadataTests`**: OUTPUT.md §6 items (EXIF copy, orientation, GPS strip,
   no stale thumbnail, ICC present, subsampling switch).
8. **`BaseLoaderTests`**: DECODE.md §7 items; PPM16 byte-order unit test with a
   synthetic buffer; HEIC-does-not-hit-LibRaw via log capture.
9. **`RenderDetailTests`**: chroma NR preserves luma and alpha; a seeded noise image
   rendered as one band and as forced non-divisible bands is bit-identical at box
   radii 1 and 3.

## 5. Performance (informational, not gating)

Opt-in local `HAPPY_PHOTON_PERF=1` diagnostics cover preview base decode, slider-tick
render at 1600px, and full export of one raw; normal CI does not enable them. Track
results in PR descriptions when a work package touches the hot path; hard budget only
for slider tick (≤ 150 ms dev baseline, RENDER.md §10).

The direct full-resolution detail diagnostic warms the optimized kernel, then uses a
5472×3648 synthetic image and reports elapsed time and peak private-memory delta:

```bash
HAPPY_PHOTON_PERF=1 dotnet test Tests/HappyPhoton.Tests.csproj -c Release --filter RenderDetailPerformanceTests
```

Run the following to decode the ISO 6400 fixture at Off, Light, and Full and report
full-decode time, center-crop chroma variation, and pixel deltas:

```bash
HAPPY_PHOTON_FBDD_EVAL=1 dotnet test Tests/HappyPhoton.Tests.csproj --filter RawFbddEvaluationTests
```

It is an explicit opt-in diagnostic because all three modes require full RAW decodes.

The LibRaw resolver's single-file extraction path has a committed publish smoke. Run
the matching command on Windows or Linux; it restores in locked mode, publishes a
self-contained single-file console, decodes the Canon fixture, and asserts that both
the bridge and LibRaw companion loaded from the runtime extraction directory:

```powershell
./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier win-x64
./scripts/verify-libraw-single-file.ps1 -RuntimeIdentifier linux-x64
```

## 6. CI

The three-platform workflow runs both xUnit v3 test hosts. Ordinary and native bitmap
integration tests live in `Tests/HappyPhoton.Tests.csproj`; UI and dispatcher tests run
through the supported Avalonia headless integration in
`HeadlessTests/HappyPhoton.Headless.Tests.csproj`. Keep Windows WIC coverage in the
ordinary host so the native and headless Avalonia platforms never share a process.

Platform and codec gaps use xUnit v3 native runtime skips (`Assert.Skip` or
`Assert.SkipWhen`) with an explicit reason so they remain visible in logs. CI gates on
discovery before execution: 666 ordinary tests plus 35 headless tests. The full run
currently expands theories to 720 execution cases. Run tests with a 90-second blame
hang timeout while changing either host.

Golden assets and baselines must keep the repo clone under control — if the goldens
directory exceeds ~20 MB, shrink render size before reaching for LFS.
