# Tone Engine: the AgX Crossing

The single scene→display crossing and the tone engine behind the tonal
sliders. This document is the formula authority and clean-room provenance
record for the crossing. It was built from Troy Sobotka's published AgX
material only; the GPL implementations (Blender, darktable, RawTherapee) were
never consulted — the Blender *binary* serves as a test oracle, never as a
source.

## 1. Two regimes, one vocabulary

The tone stage has two regimes, selected by source kind. There is no
persisted setting or UI toggle; a per-image toggle may be added later, but an
automatic exposure-based switch is rejected — the rendering would change
families discontinuously at the threshold.

- **Crossing ON — scene-referred sources (RAW).** The tone LUT is the engine
  below. The former display-domain operators (shoulder, brightness/contrast/
  shadows/highlights polynomials, base look) do not run.
- **Crossing OFF — display-referred sources (JPEG/HEIC/TIFF and the sRGB
  proxy).** The display-domain operator chain (RENDER.md §5) runs on encoded
  Rec.2020, with the target convert in finalization. Unedited output renders
  back within 1 LSB at 8 bits for every source class (sRGB JPEG and the proxy
  against their source codes; profiled/HEIC/TIFF against the frozen references
  in `Tests/assets/crossing-off-identity.json`).

## 2. Render placement

```
2 Matrix       Mn = (M_inset · M_WB) / fold                scene-linear Rec.2020
3 Tone LUT     gain 2^(EVuser+EVsource)
               → log2 window (+ log2(fold) refund, once)
               → sigmoid                                    the crossing
               → u^2.2 → E → channel curve → master curve → D  display, 1D
4 Matrix       AgX outset → E                               one Q16 write
```

Stages 2–4 run as one fused pass: `double` throughout, a 65,536-node
analytically computed LUT evaluated by linear interpolation on the unrounded
matrix output, and exactly one rounding — the final Q16 write. Interpolation
error against the analytic chain is gated at ≤ 1 Q16 LSB. `fold` is the
render normalization of the composed matrix; asShot keeps `M_WB = I` and
`fold = 1` exactly. The four user curves keep their familiar sRGB-encoded axis.
For channel `c`, the curve seam is `u_c = master(channel_c(t_c))`; a missing
channel curve is identity. The curves remain before the AgX outset, so the
outset can mix their results. Crossing-off uses the same ordered seam without
an outset.
Crossing-off sources run the same fused pass degenerately (WB matrix only,
no outset, fold refund in the gain). The working→target convert (sRGB or
Display P3) runs after the display-referred effects substep in finalization;
neither feeds back into the tone engine — see OUTPUT.md.

## 3. Fixed points

- **Middle grey:** the post-gain scene value `a = v · 2^(EVuser+EVsource)` at
  0.18 maps to display-linear 0.18 exactly, invariant under Contrast,
  Highlights, and Shadows (their terms are zero at the pivot). `EVsource`
  estimation therefore anchors tonal placement; the estimator models this
  default render, and a missing or unusable embedded preview degrades to a
  defensible default, never to per-image slider drift.
- **Achromatic in → achromatic out** through inset + sigmoid + outset (all
  matrix rows sum to 1).
- **Monotone and bounded upstream:** strictly monotone per channel for every
  slider combination before user curves; output remains in [0,1]. Identity or
  monotone user curves preserve monotonicity. Decreasing user-curve segments
  are accepted as deliberate intent.

## 4. The crossing, exactly

The sigmoid is Jed Smith's tunable sigmoid (desmos.com/calculator/yrysofmx8h;
Python port published in `sobotka/AgX-S2O3`): a linear segment of slope `s`
through pivot `(x_p, y_p)` joined to power-hyperbolic tails
`q(z, p) = z / (1 + z^p)^(1/p)` scaled to reach limits (0,0) and (1,1).

| Constant | Value | Source |
|---|---|---|
| log2 window | EV −10.0 … +6.5 around 0.18 | `sobotka/AgX` config vars −12.473931/+4.026069 |
| x_pivot | 10/16.5 = 0.6060606060606061 | grey's position in the window |
| y_pivot | 0.18^(1/2.2) = 0.4586564468643811 | `SB2383` default; see note |
| neutral slope / toe / shoulder | 2.0 / 3.0 / 3.25 | `AgX-S2O3` generator |
| post-sigmoid decode | u^2.2 | AgX 2.2 EOTF convention |
| display-linear grey at neutral | exactly 0.18 | E_sRGB 0.4613561295, Q16 30235 |

With the original y_pivot 0.5 this family reproduces the released
`AgX_Default_Contrast.spi1d` to 5.0e-8 (the derivation test keeps that
authenticity check). The shipped y_pivot uses Sobotka's later grey-preserving
default instead: scene grey maps to display grey, and agreement with
Blender's AgX on the neutral axis improves from 4.25 to 0.005 ΔE00 at grey.

**Inset:** Sobotka's published construction — rotate each Rec.2020 primary
about D65 in CIE xy, scale outward by 1/(1−0.20), take RGB→RGB(base →
enlarged) — a 20% uniform chroma compression with rotation. The rotation
angles **(+4.75°, −4.25°, +4.5°)** are derived here, not copied: they
minimize maximum OKLCh hue drift over the validation sweep subject to a
non-negative inset (without rotation, pushed blue drifts 30.7°; with it,
7.3° — better than Blender's 10.0°). The outset is the exact inverse.

```
M_inset (Rec.2020, D65, compression 0.20, rotation +4.75/−4.25/+4.5°)
 +0.9722125648757899  +0.0005798182049564  +0.0272076169192538
 +0.0236356386540170  +0.8511231029574200  +0.1252412583885629
 +0.0809977588044689  +0.0815268062292870  +0.8374754349662439

M_outset = M_inset⁻¹
 +1.0313429748257903  +0.0025432830319416  −0.0338862578577319
 −0.0141655132770723  +1.1919580980795306  −0.1777925848024580
 −0.0983689754038757  −0.1162810669486410  +1.2146500423525171
```

**Slider maps** (existing names and ranges; every constant above and below is
re-derived from its construction by a test):

```
Contrast   c ∈ [−100,100]:  slope(c)      = 2.0  · 2^(c/200)
Highlights h ∈ [−100,100]:  p_shoulder(h) = 3.25 · 2^(h/100)
Shadows    s ∈ [−100,100]:  p_toe(s)      = 3.0  · 2^(−s/100)
```

The minimum slope 1.414 clears the binding pivot-to-limit chord (1.374).
Property tests hold all §3 fixed points across the full slider grid.

### 4.1 Whites and Blacks (OPS-WP2 frozen ramps)

Both regimes share a pre-tone, ratio-preserving Rec.2020 luminance gain. For the
post-global-exposure scene luminance Y, let e = log2(Y/0.18). Whites uses the positive
half and Blacks the negative half; each is exactly zero at grey and on the other half.
For t = clamp(abs(e)/span, 0, 1), RAW uses smoothstep t²(3−2t), spans +6.5/−10 EV,
and a maximum of 4 EV per slider. Standard uses spans +log2(1/0.18)/−5.5 EV and
maximum 2.4 EV. Its C1 ramp has quadratic end caps of normalized width d = 0.02:

- t < d: t² / (2d(1−d));
- d ≤ t ≤ 1−d: (t−d/2)/(1−d);
- t > 1−d: 1−(1−t)²/(2d(1−d)).

The gain is 2^(W·sW(e) + B·sB(e)), where W/B are slider/100 times the regime's
maximum EV. Tables store log2 gain from −10 to +7 EV with 2048 mantissa intervals
per octave and constant tails. IEEE exponent/mantissa indexing needs no per-pixel
logarithm; interpolation is linear in luminance within each interval. Grey has its
own exact table node. The worst negative
log slope stays positive: RAW ≥ 1−4·1.5/6.5; standard ≥
1−2.4/((1−0.02)·log2(1/0.18)). Property tests include table nodes.
The neutral tone's 95% display point reaches ≥99% at Whites +100; its 5% point
reaches ≤1% at Blacks −100. Zero and grey are fixed.

Global gain and mask-weighted local gains use the same post-exposure luminance:
G = 2^(logGglobal + sum(weight · logGlocal)), evaluated with one exp2 after all locals.
Scalar gains commute with the existing linear local color/exposure matrices, so they
are applied together before inset/tone without extra buffers. Range classification
retains the pre-exposure, pre-operator WB basis. A zero accumulated log gain skips
exp2 and multiplication and keeps the pixel's existing tone route: clamped LUT for
unchanged pixels, analytic evaluation for previously adjusted locals. No active
controls means no additional pixel access. Monochrome retains equal channels.

Pixels with nonzero log gain use `ExtendedToneLut`, sampled from the same extended
analytic evaluators (including channel and master curves). It uses 2048 mantissa
intervals per octave from 2^-24 to 2^24, with analytic tails outside that domain,
so stacked gain never clips an intermediate. Cells crossing either user curve's
segment boundaries use analytic evaluation, including narrow nonmonotone peaks.
Quarter-cell error probes also select analytic fallback above 1e-8 output error,
including clamp knees. Oracle sweeps cover nodes, interiors, tails and final RAW outset/Q16
encoding to a one-code bound. These tables are lazily cached by the existing tone
LUT identity: Whites/Blacks ticks reuse them because those controls are not tone
parameters. Weak keys tie their lifetime to the existing bounded tone caches;
curves are snapshotted for the analytic tail. Build cost is reported separately.

## 5. sRGB / Display P3 agreement

Every shared stage runs before the target fork, so in-gamut edited content
agrees between sRGB and Display P3 within mean ΔE00 ≤ 0.034 (synthetic worst
case) and ≤ 0.053 (real-RAW full-combo edit), measured at the renderer's Q16
boundary with sharpening off and on. Encoded 8-bit files cannot carry this bound:
quantizing identical colors to different target codes introduces additional error.

## 6. Luma authority

`Rec2020Luminance` exposes the exact Rec.2020→XYZ Y row
(0.2627002120112671, 0.6779980715188708, 0.0593017164698620). Capture
sharpen, chroma NR, and output sharpening all reference it; the rounded
BT.709 constants are retired.

## 7. Retired operators

Brightness and base look are ignored by RAW's crossing-on regime but remain persisted
and functional for standard sources. Parsing never rewrites them.

## 8. Clipping semantics

Source highlights bypass both tone regimes; the display floor follows finalized
pixels. RENDER.md §7 owns source capability, predicates, projection, and mask semantics.

## 9. Validation

- **Properties:** the §3 fixed points and slider-grid checks.
- **Derivation:** every constant re-derived from its stated construction.
- **Blender oracle:** Blender 4.5.12 LTS (build `84afd5f785f7`, zip SHA-256
  `317ef64e…c34b4f`, matching Blender's published manifest) run headless over
  a 112-vector sweep (`Tests/assets/agx-blender-oracle.json`); each side is
  decoded through its own display transfer to display-linear, then compared
  as ΔE00. Tolerances: neutral axis mean ≤ 0.75 / p99 ≤ 2.1 / max ≤ 2.2,
  chromatic mean ≤ 5.0 / p99 ≤ 12.0 / max ≤ 13.0, grey ≤ 0.05. The chromatic
  residual is Blender's differently tuned inset.
- **Quality gates:** over 0→+6 EV sweeps of the BT.709 primaries and
  secondaries: OKLCh hue drift ≤ 8° at every step and chroma monotone non-increasing
  above scene white, ≤ 65% of its 0 EV value by +6 EV.
- **ColorChecker:** the characterization anchor measures the pre-crossing
  scene-linear seam (cross-platform bounds mean ≤ 3.0 / max ≤ 6.5); a second
  observation through the default crossing pins look drift (≤ 6.0 / ≤ 14.0).
  TESTING.md holds the mechanics, golden attribution, WYSIWYG bounds, and
  performance protocol.

## 10. Sources

- `github.com/sobotka/AgX` — the released config: the log2 allocation and
  the reference contrast LUT.
- `github.com/sobotka/AgX-S2O3` — its generator: the inset construction,
  window, pivots, slope, and powers, and the sigmoid port.
- `github.com/sobotka/SB2383-Configuration-Generation` — the later
  generator: the inset/outset principle and grey-preserving pivot default.
  (None of these repositories carries a license; they are cited as published
  references for constructions and constants — no code is copied.)
- Jed Smith — the tunable sigmoid, desmos.com/calculator/yrysofmx8h.
- ITU-R BT.2020-2 (primaries, luminance row); IEC 61966-2-1 (sRGB transfer);
  Björn Ottosson — OKLab/OKLCh (quality-gate metric space).
- Blender binary — behavioral oracle only; its sources are GPL and were not
  consulted.
