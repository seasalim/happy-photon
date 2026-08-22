# Pipeline Spec — UI: Controls, Gating, Interactions

UI surface for the pipeline. Follows AGENTS.md UI conventions and
`docs/DESIGN.md` tokens throughout: `CompactSlider` for edit controls, uppercase
`section-label` group headers, 20px between groups, theme tokens only (active states =
`PrimaryContainer` cyan, passive edit badges = `Tertiary` lavender), no hardcoded hex.
`ViewerSurround` is the variant-specific image surround; `AssessmentGray` and
`AssessmentWhite` are the invariant color-assessment references. They are deliberately
distinct from themed resources and must not be aliased. Color assessment mode is a
session-only viewer composition and never changes render settings or output pixels.
View markup in `Views/`, state in `MainWindowViewModel` partials (add to the matching
workflow partial, don't grow the root file).

## 1. Principles

1. Controls **write `EditSettings`; the render reacts.** No control triggers pipeline
   work directly; Auto-anything is a button that computes values once and stores them
   (OVERVIEW.md invariant 2).
2. **Capability gating**, not file-extension branching: raw-only controls bind to the
   loaded `BaseImageInfo.IsRawSource`. Before the base arrives, gate provisionally on
   `ImageFile.IsRaw`; if LibRaw cannot produce a base, keep the RAW identity and show
   the reason rather than silently demoting the file.
3. Coachmarks/tours are untouched and must not gain steps for these controls
   (existing rule: tours never mutate edits).

## 2. Develop right panel — target layout (top → bottom)

The right pane is mode-differentiated. In Library it is a **review pane** — the
fixed thumbnail histogram, the metadata/EXIF block, and a selection summary —
with no editing controls; everything below is a Develop-only surface (Library
editing surfaces remain a non-goal, §10).

```
CAMERA PROFILE         (RAW only, collapsed child control)
  [profile ComboBox]
  [Browse…] [Refresh]                         status / loading
WHITE BALANCE
  [mode/preset ComboBox]  [Auto button]  [eyedropper button]
  Kelvin   ────────●────────   5500K
  Tint     ──────●──────────   −12
ADJUSTMENTS            (no Temperature slider)
  Exposure / Brightness / Contrast / Saturation / Vibrance / Shadows / Highlights
  Recovery                                                [Clip | Blend]  (RAW only)
TONE CURVE             [RGB | R | G | B] [embedded Reset]
COLOR MIXER                                      RESET
  [Red Orange Yellow Green Aqua Blue Purple Magenta swatches]
  Hue / Saturation / Luminance        (selected band, −100..100)
DETAIL
  Sharpen  ────────●────────   25
  Noise Red.                                [OFF | LIGHT | FULL]  (RAW only)
  Chroma NR ──────●──────────   0
EFFECTS
  Vignette ─────●────────────  −35
  Midpoint ────────●─────────   50
  Grain    ───●──────────────   20
  Size                                      [FINE | MED | COARSE]
OPTICS
  Distortion                                      [toggle]
  Chromatic Aberration                            [toggle]
  Vignetting                                      [toggle]
  LENS · EMBEDDED DNG OPCODES                     source
DEVELOP FOOTER
  [Before/after] [Undo] [Redo]                         RESET
```

The adjustment stack scrolls beneath the histogram while the Develop footer remains
fixed. Export intentionally has no pointer action in Develop: return to the Library
header to click **Export**, or use the global `Ctrl+E` shortcut from either workspace.

Brightness is disabled (not hidden) at `DisabledOpacity` while a RAW base is active,
because the crossing-on engine has no Brightness parameter; it stays enabled for
JPEG/HEIC/TIFF/proxy sources. The gate follows the loaded base, falls back
provisionally to `ImageFile.IsRaw` before load, survives filmstrip switching, and
never clears the persisted value. Base look remains persisted/MCP-settable but has no
panel control; RAW ignores it and standard sources retain it.

Selection clears the previous Develop bitmap, scopes, clipping, and RAW histogram in
one provisional outcome while seeding filename-derived RAW capability, profile
visibility, and the 5500 K/6504 K as-shot placeholder. A settings-matched cached preview
may first publish its bitmap, display histogram, waveform, and display-floor clipping
as one outcome without source access. The first accepted decode outcome confirms or
corrects source facts and publishes the fresh bitmap, display scopes, clipping,
measured as-shot anchor, and sensor histogram together. A stale-base interim paint may
update bitmap and display scopes only; it clears clipping and cannot replace
decode-derived facts. A failed load outcome contributes failure status only: it paints
nothing and preserves the surface, scopes, and clipping already on screen.

Recovery is a compact, exclusive Clip/Blend control directly below Highlights, enabled
only for RAW sources — provisionally from `ImageFile.IsRaw`, then from the loaded base
fact. The row stays present and dims to `DisabledOpacity` when unavailable, so the
panel does not reflow across mixed-source filmstrips; a contradictory non-RAW loaded
fact disables the row without changing the stored value. Clip is the default.

The always-expanded Color Mixer follows the tone curve and applies to every source,
with no RAW chip or reflow. Eight code-drawn circular swatches select the working
band; the selection resets to Red when the active image changes and is never
persisted. A touched band shows a dot. Hue tracks tint through the neighboring band
hues, while Saturation and Luminance tint within the selected band. Its three
−100..100 `CompactSlider`s reset individually on double-click; the Develop footer
Reset clears all eight bands with the other color and tonal adjustments.

Detail follows the color mixer. Sharpen and Chroma NR are 0–100 `CompactSlider`s for
all sources; Sharpen displays the resolved source default (RAW 25, standard 0). Noise
Red. is an Off/Light/Full segmented control that stays in place with a RAW chip and
dims to `DisabledOpacity` for standard sources. Loaded-base capability reconciliation
never reflows the panel or discards stored values.

Effects follows Detail and applies to every source, with no RAW chip. Vignette is a
−100..100 bipolar `CompactSlider`; Midpoint is 0..100 (default 50) and remains in place
at `DisabledOpacity` while Vignette is zero. Grain is 0..100. Size is the standard
compact segmented idiom: `SurfaceHigh` container, radius 4, padding 2, height 22,
flat borderless pills, `PrimaryContainer` selected fill, and Fine/Med/Coarse labels in
FontLabel 9 SemiBold with letter spacing 1. Medium is the default.

Optics follows Effects at the tail of the scrolling edit stack. Its three fixed-height
toggle rows control distortion, lateral chromatic aberration, and corrective
vignetting; the latter is distinct from the aesthetic Effects vignette. The muted last
line identifies the embedded lens prescription and source. A missing prescription
reads `NO CORRECTION DATA FOR THIS LENS`; unavailable individual corrections dim their
rows, and the complete group dims without hiding for JPEG/HEIC and other standard
sources. These states never change the panel height.

The tone-curve selector shares the curve control's existing header, so the panel and
control do not grow. RGB is the default. Selecting an untouched R/G/B channel shows a
detached identity draft; only a committed edit creates channel state. A present channel
tints its selector letter. While a channel is active, its curve uses the matching color
label token and the composite is painted dimly behind it. The embedded Reset clears
only the active curve; RGB remains a required identity curve, while resetting R/G/B
returns that optional field to null.

When the generation-matched preview or refresh outcome installs
`BaseImageInfo.IsMonochrome`, Develop disables and dims the camera-profile picker,
every white-balance surface and command, Saturation, Vibrance, the color mixer, and
the R/G/B curve selectors. RGB/composite remains enabled. Installation atomically returns an active
color channel to composite, and both the ViewModel and curve control reject later
color-channel selection. Stored color edits remain untouched. The first false-to-true
installation shows one shared transient status message; capability is never inferred
from extension or camera model.

The camera-profile child control is visible provisionally for a RAW `ImageFile` and
confirms or retracts against the loaded `BaseImageInfo.IsRawSource`. Camera identity
starts cached local Adobe discovery in the background; opening the picker performs a
generation-correlated fallback refresh, adds embedded candidates, and shows its
pending state. Order is persisted user file, DNG embedded, matching Adobe profiles
A–Z, then built-in. Browse adds one local `.dcp`; Refresh invalidates discovery
metadata and re-resolves the selection. A terminal empty line waits for both the
current-identity Adobe scan and the image-profile pass; a completed Adobe scan with
readable profiles but no identity matches reports the probed count instead of claiming
the machine has no profiles. Until the required scope completes, the status stays neutral: awaiting
camera identity while none has arrived, scanning otherwise. Hand-picked entries show their trimmed, otherwise verbatim declared
camera model as muted subtext and in the closed-row profile/body/source tooltip.
Once any RAW decode completes without a usable camera identity, including monochrome
decode, the pending line settles to an unavailable status because camera-matched
discovery cannot run.
Loading, honest empty, unavailable, corrupt,
hash-mismatch, unsupported, and missing-WB fallback are terminal visible states;
invalid persisted choices remain selected with their reason while decode uses built-in
characterization. The control never offers hydration or causes a cloud placeholder to
be read.

## 3. White balance group

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
  The toggle changes requested intent immediately, but its visible active state follows
  only an accepted render outcome. A second toggle inverts the requested intent even
  while the first render is pending. Edit mutations request edited intent; maintenance,
  cache, refresh, and resting work preserve it, so a late edited render cannot exit an
  accepted before view.
- **Crop-mode vignette exception:** crop mode deliberately renders the full canvas so
  the overlay remains aligned. Vignette is centered on that transient full-canvas
  preview and recenters on the committed crop after Apply; pending crop coordinates do
  not enter a render request.
- **Eyedropper mode** (`W` or button, Develop only): crosshair cursor; left-click
  samples per WHITE_BALANCE.md §7 and exits the mode; Escape or re-press exits without
  sampling; pan/zoom gestures remain live (click-without-drag samples, drag pans).
  Rejected picks (clipped/noise-floor) show a status-bar hint ("Pick a neutral mid-tone
  area") and stay in the mode. Unavailable while the crop overlay is active.
- **Clipping overlay** (`J`, Develop only): latches source-saturation red and
  display-floor blue over the photograph. Hovering an available display-histogram triangle peeks
  that side only; while latched it temporarily isolates the hovered side, then restores
  both on leave. RAW uses exact sensor saturation; JPEG/HEIC use encoded near-endpoint
  samples. TIFF, PNG, and other formats disable only the red triangle as unavailable.
  The latched image carries one muted, chrome-less `CLIPPING · HIGHLIGHTS / FLOOR` line;
  toggling also uses the standard 1.5-second feedback toast.
- **Zoom is device-true and original-relative.** `ZoomLevel = 1.0` maps one original
  image pixel to one device pixel, independent of the monitor's render scaling, and
  the mouse wheel keeps the image point under the pointer fixed while zooming. The
  ViewModel owns this stable user-facing value; the view derives the current
  bitmap-relative scale from decoded original dimensions, so a 1600-to-resting source
  swap leaves both the zoom slider and on-screen scene geometry unchanged. Fit/manual
  state is shared by Develop and fullscreen; Fit calculates in device pixels and stays
  geometry-identical across source swaps. Fit and zoom-in publish the current view's
  required device-pixel long edge for resting rendering; pan and zoom-out do not
  rerender. A monitor-scaling change recomputes the same geometry and bound.

## 5. Scope box + preview activity

- **Scope box**: the Develop panel's top slot is a scope box whose
  header — the effective-scope title beside a row of three always-present icon
  toggles (mound = histogram, CFA mosaic = RAW, scanlines = waveform) — picks
  exactly one body: display histogram (default), luminance waveform, or RAW
  sensor histogram. The histogram plot itself is
  frozen: bins, channel colors, geometry, and 80 px height do not change.
  Alternate bodies may grow the box vertically only while selected, absorbed by
  the adjustment scroll area. Scope selection is session-only VM state. Scopes
  are Develop/fullscreen-only: the Library review pane shows only the fixed
  thumbnail histogram — never a waveform or RAW data. When sensor data is
  unavailable (JPEG, Library, cloud-only, unsupported-CFA, stale-base,
  replacement-in-flight), the RAW entry stays disabled in place — never removed —
  with a reason-specific `ToolTip.ShowOnDisabled` tooltip while display data shows
  as `HISTOGRAM`; the UI never labels display-referred data RAW. A selected RAW
  scope remains the session preference across those fallbacks, and a replacement
  refresh carries the matching base's RAW fact so it reactivates without another
  click. RGB parade is deferred until luminance waveform usage demonstrates demand.
- **RAW clipping indication**: effective sensor mode draws the existing red, green,
  and blue channels without a luminance line. Each channel shows a dot and the
  percentage of photosites at or above LibRaw's sensor white level; the exact
  per-channel count (and white level) live in the tooltip, matching how darktable,
  RawTherapee, Lightroom, and Capture One surface clipping visually rather than as raw
  counts. A lit channel never rounds to 0.00% — it floors to `<0.01%`. A channel dot is
  fully lit at 16 photosites and above; below that it remains dim. Display-domain
  histograms never show these dots.
- **Display clipping triangles** flank only the Develop display histogram. The right
  triangle lights for the source-referred `HighAny` fraction when the loader supplied
  a source-saturation artifact: exact sensor saturation for RAW, or encoded near-white
  samples for JPEG/HEIC. TIFF, PNG, and other unsupported formats show that side as
  unavailable and disable its peek interaction. The left triangle lights for `LowAll`
  finalized display-floor clipping on every source and remains available without a
  source-saturation artifact. A settings-matched cached outcome may light only the
  display-floor side; source-highlight availability waits for matching fresh analysis.
  Missing or stale render statistics darken both immediately.
- **Preview activity**: the scope box has no local progress surface. The existing static
  background-activity status segment shows **Preparing preview** only after its 400 ms
  hysteresis and remains active through the complete fresh entry task (profile/base
  acquisition plus first coherent render). Sliders stay **enabled** — edits accumulate
  in `EditSettings` and the first render catches up. There is no modal or disabled
  panel.

## 6. Reset / undo / presets / copy-paste scope

- **Reset** returns: `wb → asShot`, `baseLook → null` (source default), all four
  curves to identity (the three optional channel fields to null),
  `hlReconstruction → clip`, `mixer → null`, `detail → source defaults`,
  `effects → null`, plus all existing fields.
  A selected camera profile returns to built-in. One undo step, as today.
- **Undo/redo**: each committed control change is one step (existing granularity),
  including a full curve drag, point removal, or embedded curve reset;
  this includes each Clip/Blend or camera-profile selection; mode switches (preset
  select, eyedropper pick, Auto) are each one step.
- **User presets** capture color, tonal, color-mixer, all curve, detail, and effects fields and
  still never geometry or camera profiles. Hover, apply, and untoggle preserve the
  current profile.
- **Copy/paste** (`Ctrl+Shift+C/V`) carries the same widened set, including the mixer
  and nullable channel curves but never camera profiles; geometry still never
  transfers; Library multi-paste confirmation flow unchanged.

Recovery has the RAW-only Clip/Blend control and defaults to Clip. Detail fields use
the controls in §2; copy/paste preserves nullable capture-sharpen semantics.

Rendered thumbnails remain navigational chrome: the existing path detaches the
accepted finalized preview, resizes it to at most 512px, and reuses the current caches.
Vignette remains scale-invariant, while grain may be resampled in this non-authoritative
surface. Develop preview and export are the authoritative effects surfaces.

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

The dialog may open with no selected images without changing selection; in that state
it explains how to select photographs, disables Export, and offers Close. While an
export runs, configuration controls are disabled, progress and the current filename
are shown, and Cancel Export requests cancellation without destroying the dialog.
Success closes the dialog; cancellation restores the configured form; failures remain
visible, with partial completion showing the exported count and every failed filename.
A selected profile that became missing, unavailable, corrupt, or hash-mismatched
exports with built-in characterization and reports its per-image warning. Overwrite
and original-file collision confirmations are owned by the export dialog.

The final workflow-tour coachmark remains in Library; its primary action ends the tour
before opening the dialog through the normal guarded command. The modal contains no
coachmark, and closing it does not restore the completed tour step. When the tour has
no export selection, the dialog still shows the complete configuration surface and
relabels its primary Export action to **Return to Library**; it never starts an export.

| Control | Spec |
|---------|------|
| "Strip location data" checkbox | Persisted app setting, default **off** (keep GPS). Applies to both UI and agent exports. |
| "Output sharpening" checkbox | Default **on**; persisted alongside existing export preferences; applies to sized variants only (OUTPUT.md §3). |

No UI for quality-dependent chroma subsampling — it is automatic and stays invisible.

## 8. Keyboard

| Key | Action | Scope |
|-----|--------|-------|
| `W` | Toggle WB eyedropper | Develop only |
| `J` | Toggle clipping overlay | Develop only |
| `Ctrl+B` | Toggle color assessment mode | Develop/fullscreen |

Shortcut registrations belong in
[`Views/ShortcutCatalog.cs`](../../Views/ShortcutCatalog.cs); a binding change
updates its catalog entry in the same PR. The Help & About dialog reads that
catalog directly, with the shortcut tab selected by default. Library mode
ignores Develop-only keys.

## 9. Status bar

Transient hints remain in the existing message area: eyedropper active hint
("Click a neutral area — Esc to cancel") and the rejected-pick message (§4). Persistent
reasons outrank transient hints: source availability, selected RAW decode failure, then
global RAW runtime degradation. Outcomes are correlated to image and preview generation,
so canceled or superseded work cannot pin a stale failure.

One background-activity segment may appear while sustained work is active and is
absent at rest. It summarizes the highest-priority activity with overflow and shows a
determinate bar only for capture-time analysis or export totals. Its dot and progress
are explicitly static exceptions to the pulse guidance in `docs/DESIGN.md`; the
segment contains no animation. Preview preparation uses this segment exclusively.

## 10. Explicit UI non-goals

Export is the single permitted pipeline workflow modal. There are also no collapsible
panel groups, in-app migration/what's-new dialog (release note only), Library-mode
editing surfaces (the Library right pane is a review pane, §2), exposure-range change
(±3 EV stays), or slider re-ordering beyond the absent Temperature slider. The
histogram-plot freeze and the scope-box allowances within it are normative in §5.

If a change seems to need one of these, it's a spec question first.

## 11. Acceptance (VM-level, per existing test patterns)

- Mode transition matrix: asShot → drag → custom; preset select seeds values; pick
  stores gains; Auto stores picked; each transition lands one undo step
  (`MainWindowViewModel` partial tests, like existing edit-history tests).
- Reset covers every new field; presets/copy-paste round-trip the widened set;
  geometry still excluded.
- Capability-gated controls follow loaded-base facts; Brightness specifically uses
  disable-not-hide for RAW and preserves its stored value across source changes.
- Kelvin log mapping: position 0 → 2000, 1 → 12000, midpoint ≈ 4900 (√6·2000) within
  rounding.
- Shortcut registration tests list `W` and `J`.
