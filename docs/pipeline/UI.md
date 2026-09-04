# Pipeline Spec — UI: Controls, Gating, Interactions

UI surface for the pipeline. Follows AGENTS.md UI conventions and
`docs/DESIGN.md` tokens throughout: `CompactSlider` for edit controls, mixed-case
Hanken Grotesk `section-label` group headers, 20px between groups, and theme tokens
only. Control states are monochrome; hue is reserved for semantic image data —
including burst-group identity — and errors. The edit-status badge is a muted glyph
with no background.
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

The right pane is mode-differentiated. In Browse it is a **review pane** — the
fixed thumbnail histogram, the metadata/EXIF block, and a selection summary —
with no editing controls; everything below is a Develop-only surface (Browse
editing surfaces remain a non-goal, §10).

```
Camera Profile         (RAW only, collapsed child control)
  [profile ComboBox]
  [Browse…] [Refresh]                         status / loading
White Balance
  [mode/preset ComboBox]  [Auto button]  [eyedropper button]
  Kelvin   ────────●────────   5500K
  Tint     ──────●──────────   −12
Adjustments            (no Temperature slider)
  Exposure / Brightness / Contrast / Saturation / Vibrance / Shadows / Highlights
  Recovery                                                [Clip | Blend]  (RAW only)
Tone Curve             [RGB | R | G | B] [embedded Reset]
Color Mixer                                      Reset
  [Red Orange Yellow Green Aqua Blue Purple Magenta swatches]
  Hue / Saturation / Luminance        (selected band, −100..100)
Detail
  Sharpen   ────────●────────   25
  Luma NR   ─────●───────────    0
  Chroma NR ──────●──────────    0
Effects
  Vignette ─────●────────────  −35
  Midpoint ────────●─────────   50
  Grain    ───●──────────────   20
  Size                                      [Fine | Med | Coarse]
Geometry
  Vertical / Horizontal / Aspect / Distortion       (−100..100)
Optics
  Distortion                                      [toggle]
  Chromatic Aberration                            [toggle]
  Vignetting                                      [toggle]
  LENS · EMBEDDED DNG OPCODES                     source
Develop Footer
  [Before/after] [Undo] [Redo]                         Reset
```

The adjustment stack scrolls beneath the histogram while the Develop footer remains
fixed. Export has no pointer action in the Develop pane itself: click the **Export**
tab in the mode strip, or use the global `Ctrl+Shift+E` shortcut from either workspace.
The Browse bottom toolbar places culling actions at the left and view/thumbnail state
at the right; Develop mirrors that rhythm with navigation/rotation/crop at the left
and zoom/view state at the right.

Develop previews floor capture sharpening at 1.0 screen px so Sharpen responds at Fit
and at the bounded zoom base. This deliberately overstates sharpening relative to an
export; the approximation has no warning icon.

Brightness is disabled (not hidden) at `DisabledOpacity` while a RAW base is active,
because the crossing-on engine has no Brightness parameter; it stays enabled for
JPEG/HEIC/TIFF/proxy sources. The gate follows the loaded base, falls back
provisionally to `ImageFile.IsRaw` before load, survives filmstrip switching, and
never clears the persisted value. Base look remains persisted but has no
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
nothing and preserves the surface, scopes, and clipping already on screen. A
clipping-only overlay render shares the current surface generation but applies only
while its rendered frame's settings match the painted surface — or, on the edited
surface, the currently requested settings. A transient surface (Before/After original,
preset hover) renders its own frame, so the overlay must match that painted surface; a
stale, failed, or mismatched mask is dropped and the painted clipping stands.

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

Detail follows the color mixer. Sharpen, Luma NR, and Chroma NR
are 0–100 `CompactSlider`s for all sources; Sharpen displays the resolved source
default (RAW 25, standard 0). The two NR controls default to 0 and never reflow or
change enablement when source kind changes.

Effects follows Detail and applies to every source, with no RAW chip. Vignette is a
−100..100 bipolar `CompactSlider`; Midpoint is 0..100 (default 50) and remains in place
at `DisabledOpacity` while Vignette is zero. Grain is 0..100. Size is the standard
compact segmented idiom: `SurfaceHigh` container, radius 4, padding 2, height 22,
flat borderless pills, `ControlSelected` selected fill, and Fine/Med/Coarse labels in
FontBody 9 SemiBold without tracking. Medium is the default.

Geometry follows Effects and applies to every source, with no chip or capability
gating. Vertical, Horizontal, Aspect, and Distortion are −100..100 bipolar
`CompactSlider`s with unit steps and double-click reset. They are image-specific:
history and global Reset include them, while copy/paste and presets leave the target
values unchanged. Crop mode and its overlay use the corrected frame directly.

Optics follows Geometry at the tail of the scrolling edit stack. Its three fixed-height
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
metadata and re-resolves the selection. A chosen storage item that cannot provide a
local path reports that local-file requirement instead of being discarded silently.
A terminal empty line waits for both the
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
| Eyedropper button | Toggles viewer sampling mode (§4). Active state uses the standard `ControlActive` treatment. |

## 4. Viewer interactions

- **Windows display color management:** Develop, Before/After, Compare, Loupe, and
  fullscreen viewer surfaces show a display copy converted from their retained
  canonical bitmap to the current monitor's supported matrix/TRC ICC profile. Moving
  the window to another monitor re-resolves the profile and rederives surfaces without
  a source read or render. Identity cases show the canonical object directly.
  Thumbnails and placeholders remain unmanaged in this slice.
- **Before/after** (`\` or the Develop footer eye): shows the original while active.
  The original reverts tone and color only; the whole geometry family — rotation,
  horizon, crop, geometry, and lens corrections — survives, so before and after stay
  registered and lens corrections the user turned off (or a Legacy lens baseline) are
  not silently reapplied. Every path that paints an original builds those settings
  through one shared builder, differing only in the frame it passes: live edit state
  for the toggle and the clipping overlay, the ImageFile for a preview reloaded by a
  workspace transition or source hydration that leaves the original intent standing.
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
- **Alignment grid** (Develop only): changing Vertical, Horizontal, Aspect, or
  Distortion temporarily fades in a dense, near-square grid over the corrected
  image bounds. It follows image pan and zoom, fades out 1.5 seconds after the
  last geometry change, and is suppressed while crop mode owns the viewer grid.
  The overlay is display-only and never enters edit settings or rendered output.
- **1:1 loupe peek** (Develop, Browse Loupe, fullscreen, and Compare): below 1:1, a magnifier cursor marks
  where holding the left mouse button briefly magnifies the currently displayed
  bitmap to one original-image pixel per device pixel under the pointer. Once
  engaged, dragging pans with the hand; moving first instead continues as an
  ordinary pan. Release, Escape, capture loss, or a photo change restores the
  preceding zoom and viewport. The chrome-less `1:1` line and the resting-render
  refinement are transient view behavior: zoom, fit, edit, persistence, and undo
  state do not move.
- **Zoom is device-true and original-relative.** `ZoomLevel = 1.0` maps one original
  image pixel to one device pixel, independent of the monitor's render scaling, and
  the mouse wheel keeps the image point under the pointer fixed while zooming. The
  ViewModel owns this stable user-facing value; the view derives the current
  bitmap-relative scale from decoded original dimensions, so a 1600-to-resting source
  swap leaves both the zoom slider and on-screen scene geometry unchanged. Fit/manual
  state is shared by Develop and fullscreen; Fit calculates in device pixels, never
  enlarges past 1:1 (a small source shows its true size, as does the Export preview),
  and stays geometry-identical across source swaps. Fit and zoom-in publish the current view's
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
  are Develop/fullscreen-only: the Browse review pane shows only the fixed
  thumbnail histogram — never a waveform or RAW data. When sensor data is
  unavailable (JPEG, Browse, cloud-only, unsupported-CFA, stale-base,
  replacement-in-flight), the RAW entry stays disabled in place — never removed —
  with a reason-specific `ToolTip.ShowOnDisabled` tooltip while display data shows
  as `Histogram`; the UI never labels display-referred data RAW. A selected RAW
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
  select, eyedropper pick, Auto) are each one step. The persistent History panel in
  the Develop left pane shows those labeled snapshots newest-first. Clicking a row,
  `Ctrl+Z`/`Ctrl+Y`, and the Develop action-bar Undo/Redo buttons all move one
  shared current position;
  later rows remain redoable until a new edit truncates them. Clear History removes
  the rows without changing the current edit. A row's context menu, or Alt-clicking
  it, returns to that snapshot and clears every later step in the same commit.
  Hovering a non-current row previews its snapshot only in the Navigator; leaving
  restores the live preview. Steps whose camera profile, lens corrections, or
  highlight reconstruction differ show no hover and start no decode; clicking stays exact.
  Rotation, horizon, crop, and manual geometry are history fields. Applying crop
  commits its crop region and any provisional horizon change as one step; cancelling
  crop commits nothing. History commands are unavailable while crop mode is active.
- **User presets** capture color, tonal, color-mixer, all curve, detail, and effects fields and
  still never geometry or camera profiles. Hover, apply, and untoggle preserve the
  current profile.
- **Copy/paste** (`Ctrl+Shift+C/V`) carries the same widened set, including the mixer
  and nullable channel curves but never camera profiles; geometry still never
  transfers; Browse multi-paste confirmation flow unchanged.

Recovery has the RAW-only Clip/Blend control and defaults to Clip. Detail fields use
the controls in §2; copy/paste preserves nullable capture-sharpen semantics and both
NR values.

Rendered thumbnails remain navigational chrome: the existing path detaches the
accepted finalized preview, resizes it to at most 512px, and reuses the current caches.
Vignette remains scale-invariant, while grain may be resampled in this non-authoritative
surface. Develop preview and export are the authoritative effects surfaces.

## 7. Export workspace

Export is the third workspace beside Browse and Develop. The mode-strip **Export** tab
and `Ctrl+Shift+E` enter it armed; image-only fullscreen continues to refuse the transition.
Its left filmstrip snapshots the Browse selection and adds per-capture include toggles
without changing that selection. The center shows the standard preview immediately.
Its **Proof** pill optionally renders the current capture through the armed recipe's
color space, output sharpening, and size while leaving the preview visible until the
proof is ready. A chrome-less bottom-left caption labels the pixels as `PREVIEW` or
`PROOF` and names the live format and color space; it adds the recipe's pixel cap only
for a sized recipe. This caption deliberately uses the mockup's smaller over-image
metrics rather than §2's panel-control segmented idiom. The right pane
arms any combination of the fixed Hi-Res, Web, and Small recipes and owns the shared
output controls. The capture × recipe count sits above a full-width primary
**Export** button.

`Enter` runs only while Export is active. Elsewhere it retains its crop-apply and
Browse/Develop meanings. `Escape` returns to the workspace active before Export and
never cancels a running export. One immutable job is owned by the main view model at a
time; it and its cancellation token survive workspace switches. Application shutdown
cancels and drains that job before image services are disposed.

Before the queue opens, one pass over every resolved target refuses loaded-original
collisions and duplicate output paths, identifies RAW+JPEG pair collisions with the
filmstrip remedy, confirms all existing-file overwrites together, and confirms the exact
cloud-source hydration scope. The workspace-local queue strip
sits above the footer and advances per capture-recipe target. It disappears outside
Export while the owned work continues and resumes from the same job when Export is
re-entered.

Completion remains in the workspace. One target-level report card shows successful
counts, failed capture-recipe pairs, and profile warnings together. **Retry failed
only** projects exactly those pairs from the immutable job, retaining its output and
edit snapshots, then runs the same preflight again. The final workflow-tour coachmark
also lives in Export and switches workspaces rather than opening a modal surface.

| Control | Spec |
|---------|------|
| "Strip location data" checkbox | Persisted app setting, default **off** (keep GPS). |
| "Output sharpening" selector | Off, Screen, or Print; persisted alongside existing export preferences (OUTPUT.md §3). |

No UI for quality-dependent chroma subsampling — it is automatic and stays invisible.

## 8. Keyboard

| Key | Action | Scope |
|-----|--------|-------|
| `W` | Toggle WB eyedropper | Develop only |
| `J` | Toggle clipping overlay | Develop only |
| `R` | Toggle crop | Develop only |
| `Shift+R` | Switch between paired JPEG and RAW | Develop only |
| `C` | Enter Compare with 2–4 selected photos | Browse grid/Loupe |
| `L` | Toggle color assessment mode | Develop/fullscreen |
| `E` / `Enter` / `Space` | Enter Browse Loupe | Browse grid |
| `E` / `G` / `Escape` | Return from Browse Loupe | Browse Loupe |
| `Space` / `Z` | Toggle Fit and 1:1 | Develop/Browse Loupe |
| `Ctrl+Space` | Toggle active photo in selection | Browse/Develop/Loupe |
| `Ctrl+Shift+E` | Enter Export | Browse/Develop |

Shortcut registrations belong in
[`Views/ShortcutCatalog.cs`](../../Views/ShortcutCatalog.cs); a binding change
updates its catalog entry in the same PR. The Help & About dialog reads that
catalog directly, with the shortcut tab selected by default. Browse mode
ignores Develop-only keys.

The About tab includes one display-profile diagnostic line. It names the active
profile and whether its matrix/TRC transform is active, Windows Auto Color Management
owns conversion, macOS owns it (the window's Metal layer is tagged sRGB), or the profile
is treated as sRGB because it is LUT-based, MHC2, or invalid. This is diagnostic text only; there are no display-profile controls.

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
panel groups, in-app migration/what's-new dialog (release note only), Browse-mode
editing surfaces (the Browse right pane is a review pane, §2), exposure-range change
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
