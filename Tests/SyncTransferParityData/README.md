# Sync transfer parity recording

G1 freezes transfer behavior before the registry refactor. Normal tests compare
against these files; they never update them. Each gzip file contains UTF-8 JSONL,
one `{Key, Value}` record per observation, ordered by ordinal key and terminated
with LF. Settings use `EditSettingsJson`'s canonical JSON. Preset bytes are Base64
of the exact saved file, using a fixed overwrite ID rather than rewriting GUIDs.
Comparison checks each record and then every decompressed byte.

The corpus in `../SyncTransferParityCorpus.cs` contains all 15 distinct settings
cases from `GoldenTestCases` (shared asset cases are deduplicated by slug), LH8,
BCap, LK, and 50 backup-generator documents. The backup sample is ten complete
five-step sessions from seed 292031, retaining crop, curves/HSL, and one or two
locals. LH8 uses the existing deterministic-ID `LocalsBrushWorkloads` factory;
BCap attaches its seeded cap strokes at 1600 × 1067. LK is checked against the
existing G2 fixture. Seven destinations cover locals, crop/straighten/rotation,
manual geometry, a user RAW profile, a lens override, and existing/deleted preset
markers. Input documents are included in `model.jsonl.gz` to detect corpus drift.

The model test records subset copy/transfer, preset save bytes/settings, and
preset reload. Headless tests replay every source against every destination via
Develop paste, Browse paste, preset hover/exit/apply, and history undo/redo.
History restore installs the source snapshot and then the destination snapshot,
so the deleted-preset normalization is exercised. A separate replay pastes every
source while crop and straighten have uncommitted drafts.

Each VM observation contains model and catalog settings, persisted history
labels/documents/position, live sliders, curves, crop, local selection/rows,
profile selection, and UI history flags. Hover also records the actual preview's
settings hash to detect changes that do not affect stored settings or controls.
Rendering uses a successful synthetic 16 × 12 base; no photograph is required.
The RAW-profile destination uses a synthetic RAW source. Pixel quality, real RAW
decoding, and G2 latency are outside this settings-parity gate.

Build with `dotnet build HappyPhoton.sln -c Release`, then run:

```powershell
./Tests/MeasureSyncTransferParity.ps1 -Runs 3
```

The runner gives each test process a 120-second ceiling and writes disposable
logs under `Tests/obj/SyncParity`. `-Case Locals -Runs 1` selects one replay.
`-Record` enables initial recording only and refuses to overwrite any existing
baseline. Baseline replacement requires explicitly removing the selected data
file; a feature implementation must not regenerate its own expected outputs.
To inspect a recording, decompress it with any gzip reader; preset bytes can be
decoded directly from the `preset/bytes/` records.
