# Render v10 perceptual-chroma attribution

Render v10 replaces the HSL Modulate saturation/vibrance operator with the
perceptual OKLCh stage. The 39 inherited settings cases all have S=V=0 and are
byte-identical to their render-v9 baselines; the identity skip does not access pixels.

Six new baselines intentionally exercise the changed operator:

- `canon-eos-350d`: saturation −50, vibrance −100, and saturation −35 with vibrance +70.
- `display-p3-reference`: the same saturation-only, vibrance-only, and combined cases.

No existing edit is migrated. Active chroma uses the render-v10 math fix-forward.
