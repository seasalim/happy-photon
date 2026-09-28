# Repairs model, persistence and rendering

`EditSettings.Repairs` is an ordered list of `heal` or `clone` entries, independent
of the eight locals. Canonical v4 JSON appends `repairs` after every existing key
and omits it when null or empty. Settings and pipeline versions stay unchanged;
repair-free canonical bytes and hashes stay unchanged. Older builds ignore the
unknown field and lose repairs on save; downgrade is not a supported round trip.
Stored repairs render through the shared pipeline; the Spots tool edits them in Develop.

Each entry requires a unique 32-hex GUID, type, destination `u`/`v`, source
`su`/`sv`, radius, feather, and opacity. Coordinates are normalized doubles,
rounded to 1/16384 after clamping to [0, 1]. They belong to the oriented,
lens-corrected base, before user geometry. Quarter turns never transform them.
Load and save reject malformed entries, unknown types, non-finite numbers,
more than 64 repairs, or summed π·radius² above the area limit (overlaps count).
Ranges clamp before the area check: radius [0.002, 0.10], feather [0, 1], opacity [0.05, 1]. Six maximum discs
are admitted; seven reject. Saving validates a clone and does not mutate live state.
Area validation uses the shared Neumaier compensated `RepairArea.Sum` over clamped
radii and admits sums ≤ `Repair.MaximumArea * (1 + 1e-12)`, keeping `MaximumArea`
exactly 0.18849555921538758 long-edge²; the relative tolerance covers rounding only
(below about 5e-13 relative radius change).

The owner's P-1 ruling moves aspect-dependent source clamping to use time.
`RepairGeometry` computes min(radius × max(W,H), min(W,H)/2) and clamps source
centres to contain that disc. It is pure and depends only on the base aspect.
The render stage calls it where the base is known; spot gestures write clamped sources.
Deserialization has no trustworthy base dimensions and therefore
only enforces aspect-independent rules. Destinations may cross the frame edge.
`BaseFrameMapping` uses x = uW − 0.5, without phase correction; see
[the FINAL registration decision and decoder evidence](HEAL_REGISTRATION.md).

History restores repairs explicitly alongside locals. Clone and version creation
isolate repair entries; the settings hash includes them through canonical JSON.
Reset clears them, while "Preset: None" preserves them. Preset saving strips
repairs and imported presets drop their payload before validation. Paste's
explicit subset leaves destination repairs untouched. `EditHistoryLabel` supplies
spot labels; callers can identify gestures such as "Resize spot" or "New spot
source" using its operation override, since the same value change can represent
more than one action.

`AutomaticRepairSource` ranks a bounded ring search on the held interactive base
by boundary mismatch and texture differences. Candidate discs lie inside the base
and do not overlap the destination; ties preserve search order. The UI writes the
winner through the shared source clamp and keeps the ranking for source cycling.
Source selection never runs during rendering and never acquires original content.

## Render stage

`RenderRepairs` writes the render-owned copy before geometry, DCP, WB and tone.
Repairs compose in creation order; source and destination boundary footprints are
snapshotted from the current pixels before each spot writes. Clone bilinearly samples
the translated source; opacity multiplies an inward smoothstep feather. Heal uses the
FINAL Membrane-additive contract in linear Rec.2020: 32 boundary squares with half-width
r/3 at radius r(1 + √2/3), and normalized discrete Poisson weights on that circle.
Source and destination row prefixes are rebuilt per spot; no frame-wide prefix exists.

Corrections preserve headroom without clamping. With directional headroom
h = max(0, (1 − s upward, s downward) − 0.51/65535), magnitudes through 7h/8 are
unchanged; larger magnitudes become 29h/32 − h²/[1024(m − 27h/32)], sign preserved.

Preview, resting, Compare/Loupe, adjacent warm, side surfaces, Proof, export and standard
thumbnails share the canonical-base stage. Resting removes repairs from its prepared
settings after geometry, preventing a second application. Before and the encoded
camera-JPEG RAW thumbnail fallback exclude repairs. Range/hue sampling and WB picking
include them; sensor clipping deliberately keeps the unrepaired sensor mask.
Null/empty repairs return before pixel access, preserving v14 renders and hashes.
