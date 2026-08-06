# Pipeline Spec — UI: Controls, Gating, Interactions

UI surface for the pipeline rework. Follows AGENTS.md UI conventions and
`docs/DESIGN.md` tokens throughout: `CompactSlider` for edit controls, uppercase
`section-label` group headers, 20px between groups, theme tokens only (active states =
`PrimaryContainer` cyan, passive edit badges = `Tertiary` lavender), no hardcoded hex.
View markup in `Views/`, state in `MainWindowViewModel` partials (add to the matching
workflow partial, don't grow the root file).

## 1. Principles

1. Controls **write `EditSettings`; the render reacts.** No control triggers pipeline
   work directly; Auto-anything is a button that computes values once and stores them
   (OVERVIEW.md invariant 2).
2. **Capability gating**, not file-extension branching: raw-only controls bind to the
   loaded `BaseImageInfo.IsRawSource`. Before the base arrives, gate provisionally on
   `ImageFile.IsRaw`; reconcile when the base loads (a raw that fell back to the
   standard loader demotes to non-raw UI and logs the fallback — never show raw
   controls that the pipeline will ignore).
3. Coachmarks/tours are untouched and must not gain steps for these controls
   (existing rule: tours never mutate edits).

## 2. Develop right panel — target layout (top → bottom)

```
WHITE BALANCE          (new group, WP3.3)
  [mode/preset ComboBox]  [Auto button]  [eyedropper button]
  Kelvin   ────────●────────   5500K
  Tint     ──────●──────────   −12
ADJUSTMENTS            (existing group, Temperature slider REMOVED)
  Base look            [toggle]         (new, WP2.2)
  Exposure / Brightness / Contrast / Saturation / Vibrance / Shadows / Highlights
CURVE                  (unchanged)
DEVELOP FOOTER
  [Before/after] [Undo] [Redo]                         RESET
```

The adjustment stack scrolls beneath the histogram while the Develop footer remains
fixed. Export intentionally has no pointer action in Develop: return to the Library
header to click **Export**, or use the global `Ctrl+E` shortcut from either workspace.

## 3. White balance group (WP3.3)

| Control | Spec |
|---------|------|
| Mode/preset ComboBox | Stable items: As Shot, Daylight, Cloudy, Shade, Tungsten, Fluorescent, Flash, Custom, and Picked. Selecting a preset writes `mode: preset` + resolved kelvin/tint (WHITE_BALANCE.md §6); As Shot writes `mode: asShot`; Custom seeds a custom setting from the displayed sliders. Picked reflects an Auto/eyedropper result and is otherwise non-actionable. The item source must not change while Avalonia processes a selection. |
| Kelvin slider | `CompactSlider`, UI position is **log-scaled**: VM exposes a linear 0–1 position mapped through `K = 2000·6^p` (2000–12000); value label shows the rounded Kelvin ("5500K", nearest 50). Shows the resolved value in every mode (as-shot estimate when `asShot`). |
| Tint slider | −100…+100, label shows signed integer. |
| Drag behavior | Dragging either slider from any mode switches to `mode: custom`, seeded from the currently displayed kelvin/tint. From gain-backed settings this **discards gains** — acceptable and deliberate; the previous state lands on the undo stack like any edit. |
| Auto button | Runs WHITE_BALANCE.md §8 once, stores as `picked`. Disabled until the base is loaded (§5). |
| Eyedropper button | Toggles viewer sampling mode (§4). Active state uses the standard `PrimaryContainer` treatment. |

## 4. Viewer interactions

- **Before/after** (`B` or the Develop footer eye): shows the original while active.
  Any path that installs edited pixels — including scheduled histogram refresh and
  preset hover/restore — clears the active state so the control always matches the
  displayed preview.
- **Eyedropper mode** (`W` or button, Develop only): crosshair cursor; left-click
  samples per WHITE_BALANCE.md §7 and exits the mode; Escape or re-press exits without
  sampling; pan/zoom gestures remain live (click-without-drag samples, drag pans).
  Rejected picks (clipped/noise-floor) show a status-bar hint ("Pick a neutral mid-tone
  area") and stay in the mode. Unavailable while the crop overlay is active.
- **Clipping overlays** (`J` or histogram chips, Develop only): composited tints at
  preview resolution — highlight clip red, shadow crush blue (RENDER.md §7 thresholds).
  One toggle controls both; state is session-only (not persisted, not in EditSettings).
  Overlays are view-layer only and never appear in exports, before/after, or fullscreen.

## 5. Histogram + base-arming indicator

- **Clipping chips** (WP4.2): small warning chips at the histogram's top corners, lit
  when `ClippingStats` fractions exceed 0.1%; tooltip shows per-channel percentages;
  clicking a chip toggles the overlays (same state as `J`).
- **Arming indicator** (WP1.3): while the linear base is decoding after Develop entry,
  show a thin indeterminate progress line under the histogram. Sliders stay **enabled**
  — edits accumulate in `EditSettings` and the first render catches up. Show only when
  the decode exceeds 150 ms (no flicker on fast paths); no modal, no disabled panel.

## 6. Reset / undo / presets / copy-paste scope

- **Reset** returns: `wb → asShot`, `baseLook → null` (source default),
  `hlReconstruction → clip`, plus all existing fields. One undo step, as today.
- **Undo/redo**: each committed control change is one step (existing granularity);
  mode switches (preset select, eyedropper pick, Auto) are each one step.
- **User presets** capture the new color/tonal fields (wb, baseLook,
  hlReconstruction) and still never geometry. Applying/untoggling behavior unchanged.
- **Copy/paste** (`Ctrl+Shift+C/V`) carries the same widened set; geometry still never
  transfers; Library multi-paste confirmation flow unchanged.

Highlight reconstruction, capture sharpening, FBDD, and chroma NR have no
first-release controls. Highlight reconstruction defaults to Clip, capture sharpening
uses its source-kind default, and FBDD and chroma NR remain Off/0.

## 7. Export dialog

Export is an owned modal dialog centered over the main window. The Library header is
the sole pointer entry. `Ctrl+E` opens the dialog from Library or Develop and remains
a no-op in image-only fullscreen mode. Opening the dialog snapshots the current export
selection and disables the workspace until the dialog closes.

The desktop dialog exports exactly one size per run through a mutually exclusive
Hi-Res, Web, or Small radio group. Hi-Res preserves the original dimensions; Web and
Small expose their existing maximum-dimension fields. Desktop output is written
directly into the chosen folder. The export engine and agent `export_images` tool keep
their existing multi-variant capability and per-variant subfolders.

The dialog may open with no selected images without changing selection. In that state
it explains how to select photographs, disables Export, and offers Close. While an
export runs, configuration controls are disabled, progress and the current filename
are shown, and Cancel Export requests cancellation without destroying the dialog.
Success closes the dialog; cancellation restores the configured form; failures remain
visible. Overwrite and original-file collision confirmations are owned by the export
dialog.

The final workflow-tour coachmark remains in Library. Its primary action ends the tour
before opening the dialog through the normal guarded command. The modal contains no
coachmark, and closing it does not restore the completed tour step. When the tour has
no export selection, the dialog still shows the complete configuration surface and
relabels its primary Export action to **Return to Library**; it never starts an export.

| Control | Spec |
|---------|------|
| "Strip location data" checkbox | WP0.3. Persisted app setting, default **off** (keep GPS). Applies to both UI and agent exports. |
| "Output sharpening" checkbox | WP5.2. Default **on**; persisted alongside existing export preferences; applies to sized variants only (OUTPUT.md §3). |

No UI for quality-dependent chroma subsampling — it is automatic and stays invisible.

## 8. Keyboard

| Key | Action | Scope | WP |
|-----|--------|-------|----|
| `W` | Toggle WB eyedropper | Develop only | 3.3 |
| `J` | Toggle clipping overlays | Develop only | 4.2 |

Shortcut registrations belong in
[`Views/ShortcutCatalog.cs`](../../Views/ShortcutCatalog.cs). Each work package
adds or changes its catalog entry in the same PR as the binding. The Help &
About dialog reads that catalog directly, with the shortcut tab selected by
default. Library mode ignores Develop-only keys.

## 9. Status bar

Transient hints only (no new permanent segments): eyedropper active hint
("Click a neutral area — Esc to cancel"), rejected-pick message (§4), and the
raw-fallback notice ("Decoded via fallback — RAW controls unavailable", §1.2).

## 10. Explicit UI non-goals

Export is the single permitted new workflow modal. No additional pipeline workflow
modals are introduced. There are also no collapsible panel groups, histogram redesign,
in-app migration/what's-new dialog (release note only), Library-mode editing surfaces,
exposure-range change (±3 EV stays), or slider re-ordering beyond removing
Temperature. If a WP seems to need one of these, it's a spec question first.

## 11. Acceptance (VM-level, per existing test patterns)

- Mode transition matrix: asShot → drag → custom; preset select seeds values; pick
  stores gains; Auto stores picked; each transition lands one undo step
  (`MainWindowViewModel` partial tests, like existing edit-history tests).
- Reset covers every new field; presets/copy-paste round-trip the widened set;
  geometry still excluded.
- Raw-only controls hidden for a JPEG; demotion on raw-fallback verified with a forced
  loader failure.
- Kelvin log mapping: position 0 → 2000, 1 → 12000, midpoint ≈ 4900 (√6·2000) within
  rounding.
- Shortcut registration test: `ShortcutCatalog.Groups` lists `W`/`J` when the
  features land.
