# Repairs model and persistence (HEAL-WP2)

`EditSettings.Repairs` is an ordered list of `heal` or `clone` entries, independent
of the eight locals. Canonical v4 JSON appends `repairs` after every existing key
and omits it when null or empty. Settings and pipeline versions stay unchanged;
repair-free canonical bytes and hashes stay unchanged. Older builds ignore the
unknown field and lose repairs on save; downgrade is not a supported round trip.
This work adds no rendering or UI.

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
WP3 must call it where the base is known; WP4 must write clamped sources from
its gestures. Deserialization has no trustworthy base dimensions and therefore
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
