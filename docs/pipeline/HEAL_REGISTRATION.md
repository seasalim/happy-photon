# HEAL-WP2 registration - 2026-09-26

**FINAL: no phase correction.** The owner's acceptance-9 ruling (option 3,
2026-09-26) sets registration to <= 0.5 full-size px for Bayer and <= 0.75 px for
X-Trans. `BaseFrameMapping` uses x = uW - 0.5 and its inverse for every decode.
There is no per-CFA phase metadata, pixel shift, or kernel correction. The
X-Trans half-size nondeterminism described below is accepted under this bound;
it has not been fixed. The spec lane must copy this ruling into HEAL Decisions
and "Preview/full registration". The earlier blocker evidence is retained below.

## Decoder evidence

The decoder source is the [LibRaw 0.22.2 release archive](https://www.libraw.org/data/LibRaw-0.22.2.tar.gz).
Its SHA-512 was checked against the committed native package provenance and
`native/libraw/ports/libraw/portfile.cmake`:

```
9333bc667c8e68a3572c336d3e2ecda82c5987e7feecb6ceb4e1df7dc7291747ffe66f6d3e01b121946ba4e2b1be95295c030d2754a5ae1cd638cffc8213141a
```

These references are to that archive, whose line numbers differ from GitHub's tag:

- `src/postprocessing/dcraw_process.cpp`, lines 41–47: X-Trans's nonzero filters
  take the `raw2image_ex` path too; the local variable named `is_bayer` is broader
  than a Bayer CFA.
- `src/preprocessing/raw2image.cpp`, `raw2image_start`, lines 49–56: half-size
  sets shrink and uses ceil(width/2), ceil(height/2).
- The same file, `copy_bayer`, lines 273–298, and its call at line 498: each source
  sample assigns its channel at (row >> 1, column >> 1). It does not average all
  samples of that channel. The OpenMP loop distributes source rows dynamically.
  Two rows can consequently assign the same X-Trans green channel in one output
  pixel without synchronization. The package enables OpenMP on Windows/Linux;
  its macOS provenance records OpenMP disabled.
- `src/demosaic/misc_demosaic.cpp`, `pre_interpolate`, lines 28–46: X-Trans
  half-size fills missing red/blue values from horizontal neighbors. Lines
  64–78 configure separate Bayer-green mixing (performed in dcraw_process.cpp
  at line 204), without mixing X-Trans greens, then clear
  filters. The half-size path therefore does not run Markesteijn to restore a
  common sampling position.

The pinned X30 CFA in `native/libraw/oracle/facts/fujifilm-x30.raf.json` has green
samples from both rows in seven of its nine half-size blocks. Tracking sample
positions through the assignments gives the following green-channel centroids,
relative to the geometric 2×2 block centre, in full-size pixel units:

| Source-row completion order | Mean X | Mean Y |
|---|---:|---:|
| Upper then lower | 1/6 | 7/18 |
| Lower then upper | 1/6 | −7/18 |

`HealDecodeFramingTests` pins this calculation, the Bayer zero green centroid,
and the X30 chroma hole. It reads only the committed metadata JSON. This is a
source-derived assignment diagnostic, not a native execution test or a model of
full demosaicing, color conversion, optical warping, or the actual thread
schedule. It demonstrates why a single deterministic phase cannot be inferred
from a serial 2×2 average. In particular, the measured +0.25 horizontal candidate
is not the derived green centroid. Choosing a vertical mean of zero or adjusting
both coordinates to match NCC would require an additional approximation policy;
that policy is not established by the decoder framing.

## Frozen registration workload

The existing `HealGateTests.Registration` and its runner were not edited.
Five fresh Release processes used exactly its Canon/Fuji fixtures, optics
on/off arms, interactive/large bases, nine green-channel NCC patches, search,
and 0.5 full-size pixel threshold. Its existing live availability checks passed
before source reads. No originals were modified or hydrated.

| Sample / process | Canon maximum | Fuji maximum | Result |
|---|---:|---:|---|
| 1 / 34016 | 0.230489 | 0.485412 | pass |
| 2 / 11224 | 0.230489 | 0.450694 | pass |
| 3 / 13824 | 0.230489 | 0.406971 | pass |
| 4 / 37076 | 0.230489 | 0.596343 | fail |
| 5 / 27020 | 0.230489 | 0.552268 | fail |

Sample 4 fails at Fuji 1600×1195 without optics, offset (0.5, 0.325).
Sample 5 fails at Fuji 1600×1195 without optics, offset (0.05, 0.55), and
2016×1506 with optics, offset (0.525, −0.075). The former cannot be brought
within 0.5 by any horizontal-only correction, including WP1's +0.25 candidate.

Subtracting the two source-derived *serial green centroid candidates* from all
captured Fuji offsets gives maximum residuals 0.589–0.703 and 0.739–0.946 px,
respectively. This is diagnostic arithmetic on the frozen measurements, not a
new gate or qualification through a production mapping. Neither candidate is
justified as the phase of the actual parallel, characterized, corrected base.
No pixels, kernel, decode settings, worker limits, or gate thresholds were changed.

All five logs remain under `artifacts/heal/`, with these timestamp stems:
`20260926-221419-9064918`, `20260926-221611-8877454`,
`20260926-221718-7951402`, `20260926-221742-0543269`,
`20260926-221805-4792461` (prefix `Registration-raw-Membrane-Additive-`,
suffix `-area-0-1.log`). The passing runs do not supersede the failures.

## Earlier blocked handoff

A decoder-derived phase satisfying the original 0.5 px X-Trans bound was not
established. The owner subsequently accepted the revised accuracy bound above;
no correction was fitted to these measurements.

Validation before that ruling:

- `dotnet build HappyPhoton.sln -c Release`: succeeded after adding the diagnostic,
  zero warnings and errors (the initial pre-change build had one existing xUnit2020 warning).
- `dotnet test Tests/HappyPhoton.Tests.csproj -c Release --no-build --no-restore --filter 'FullyQualifiedName~HealDecodeFramingTests|FullyQualifiedName~HealSerializationControlTests' --logger 'console;verbosity=normal'`:
  4 passed; G1 remains zero differing bytes across the 84 frozen documents.
- `./Tests/RunHealGates.ps1 -Gate Registration -TimeoutSeconds 120`: five invocations,
  three passed and two failed as above. The runner selects only
  `FullyQualifiedName=HappyPhoton.Tests.HealGateTests.Registration`.
- G2's 32,938-byte control was not recomputed. There is no S64 production arm yet.
  The full suite and `scripts/verify.ps1` were left to the orchestrator under rule 6.

## Qualification after the owner ruling

The unchanged frozen workload (only its per-CFA threshold and failure message
changed) passed in Release, process 44424, 24 logical processors. No phase was
applied. Maximum full-size pixel errors across the nine patches:

| Source | Optics | Interactive | Large | Threshold |
|---|---|---:|---:|---:|
| Canon EOS 6D | off | 0.230489 | 0.158114 | 0.5 |
| Canon EOS 6D | on | 0.230489 | 0.158114 | 0.5 |
| Fuji X30 | off | 0.395285 | 0.550568 | 0.75 |
| Fuji X30 | on | 0.406971 | 0.503115 | 0.75 |

Canon has no active embedded prescription in this workload; Fuji's optics-on
arm does. This run does not establish a deterministic Fuji phase or erase the
older failures under the former bound. The frozen diagnostic's +0.25 candidate
still appears in its log for comparison only and is never applied.
`RepairGeometryTests.FinalBaseMappingHasNoPhaseAtEitherDecodeSize` pins the
plain mapping, its inverse, resize equivalence and the frozen NCC patch-centre
convention; `HealDecodeFramingTests` retains the decoder-derived evidence.

Command: `./Tests/RunHealGates.ps1 -Gate Registration -TimeoutSeconds 120`.
Evidence: `artifacts/heal/Registration-raw-Membrane-Additive-20260926-231222-4852474-area-0-1.log`.
The [repairs persistence contract](REPAIRS.md) records P-1's clamp-at-use ruling.
The [HEAL-WP2 record in TESTING.md §5](TESTING.md#heal-wp2-frozen-controls) defines the frozen G1/G2 controls.
