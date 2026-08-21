# R5a golden attribution

Generated on win-x64 by comparing the frozen pre-R5a v9 set with the single R5a
re-baseline through `scripts/report-golden-deltas.cs`. Standard/HEIC goldens were
byte-identical; only the 19 RAW cases changed.

| Golden | Mean ΔE76 | p99 ΔE76 |
|---|---:|---:|
| canon-eos-350d / contrast +50 | 0.014 | 0.643 |
| canon-eos-350d / exposure −2 | 0.008 | 0.399 |
| canon-eos-350d / exposure +2 | 0.012 | 0.575 |
| canon-eos-350d / full combo | 0.015 | 0.652 |
| canon-eos-350d / highlights −100 | 0.012 | 0.599 |
| canon-eos-350d / identity | 0.012 | 0.606 |
| canon-eos-350d / shadows +80 | 0.010 | 0.457 |
| canon-eos-350d / WB 3000 | 0.011 | 0.677 |
| canon-eos-350d / WB 9000 tint −50 | 0.012 | 0.553 |
| canon-eos-350d / WB 9000 tint +50 | 0.013 | 0.584 |
| fujifilm-x30 / exposure +2 | 0.006 | 0.349 |
| fujifilm-x30 / identity | 0.007 | 0.370 |
| fujifilm-x30 / WB 3000 | 0.006 | 0.230 |
| nikon-d70 / exposure +2 | 0.003 | 0.000 |
| nikon-d70 / identity | 0.004 | 0.000 |
| nikon-d70 / WB 3000 | 0.004 | 0.000 |
| pentax-k-r / exposure +2 | 0.005 | 0.000 |
| pentax-k-r / identity | 0.006 | 0.430 |
| pentax-k-r / WB 3000 | 0.006 | 0.178 |

Maximum mean ΔE76 was 0.015; maximum p99 was 0.677. This attributes the
movement to replacing LibRaw's output-space matrix/rounding with Happy Photon's
double-precision fused characterization and one Q16 write.
