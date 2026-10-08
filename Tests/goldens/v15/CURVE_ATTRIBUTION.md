# Render v15 curve attribution

Render v15 measures each interior Catmull-Rom tangent in x, so user curves stay C¹
through unevenly spaced points (RENDER.md §5.5). Evenly spaced curves, including every
built-in look, keep their tables byte for byte.

The 65 v14 PNGs were regenerated from the same matrix. Sixty-three are byte-identical.
The two full-combo files change because their curve, (0.25, 0.20) (0.75, 0.82), is
unevenly spaced:

| Golden | Mean ΔE76 | p99 ΔE76 |
|---|---:|---:|
| `canon-eos-350d__full-combo-tonal.png` | 1.444966 | 3.706535 |
| `display-p3-reference__full-combo-tonal.png` | 1.995948 | 4.536022 |
