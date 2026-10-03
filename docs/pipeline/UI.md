# Pipeline Spec — UI: Controls, Gating, Interactions

Pipeline controls use [DESIGN.md](../DESIGN.md)'s theme, typography and assessment
resources. Views display controls; matching ViewModel partials own state and commands.
Color assessment is session-only composition and never changes output pixels.

## 1. Principles

1. Controls **write `EditSettings`; the render reacts.** No control triggers pipeline
   work directly; Auto-anything is a button that computes values once and stores them
   (OVERVIEW.md invariant 2).
2. **Capability gating**, not file-extension branching: raw-only controls bind to the
   loaded `BaseImageInfo.IsRawSource`. Before the base arrives, gate provisionally on
   `ImageFile.IsRaw`; if LibRaw cannot produce a base, keep the RAW identity and show
   the reason rather than silently demoting the file.
3. Tips never change edits, selection or filters; DESIGN.md owns their presentation.
4. Shift+wheel over enabled, unlocked sliders steps by `SmallChange`, accumulates fractional deltas, and clamps at both ends (cyclic controls stop one step below the wrapping maximum); each burst renders like a drag and commits one history step, ending before image selection changes.

## 2. Develop right panel — target layout (top → bottom)

Crop, Spots and Locals are exclusive tool modes beneath the fixed scope box. Their active
header stays fixed while tool settings enter the top of the scrolling stack. Global
edits are locked and dimmed while a tool is active; WB picking is inert. Switching
tools discards unfinished input but retains committed locals. Crop Apply commits the
crop and draft Horizon together; Cancel discards the draft.
Crop Auto sets its best estimate of draft Horizon; an unchanged draft shows “Already level”.

Spots (Q) offers Heal/Clone, Size, Feather and Opacity; controls edit the selected
spot or persistent new-spot preferences. Click chooses an automatic source; drag
chooses it manually. Destination, source and edge drags move, retarget and resize.
Each gesture is one history step. Escape cancels a draft, deselects, then closes.
Delete/Backspace never trash photos while Spots is open. Count and summed-area
limits clamp creation/resizing; Clear spots removes the list. Overlay projection
uses the displayed bitmap's base geometry, including its crop rounding.
Visualize (A) draws a high pass of the monitor-transformed visible viewport below
spot circles, including at refined 1:1; it never changes exports, statistics or caches.
Threshold is a debounced app preference. H hides circles without deselecting; / ranks
fresh source candidates. Brackets change size; Shift+brackets change feather, each
selected-spot change committing one history step. Text entry and pickers take precedence.

Masks pointer feedback follows press targeting: crosshair for armed Radial/Linear creation and rotation/direction, move for centers, axis-aligned resize for shape handles, and hand for other pins; Brush and hue-pick feedback remain active, and gesture cursors persist through capture.

Locals uses the [base orientation contract](DECODE.md#orientation).
Locals supports at most eight linear/radial/brush adjustments in creation order with shared
ordinals. Disabled and neutral locals still count; selection and Show Mask are session
state. Closing retains edits. Scopes, footer, viewer controls and filmstrip remain live.

Each local has Exposure, Whites, Blacks, relative Temperature/Tint and Saturation; monochrome disables
color rows without clearing stored values. Geometry controls edit center, angle,
feather and radial axes/polarity. Luminance and Hue Range disclosures restrict the
geometric mask. Reset adjustments clears adjustment values but preserves geometry,
enablement and ranges. Center in view moves only the local center.

Whites and Blacks are enabled for every source kind, including monochrome RAW.
Their global rows follow Shadows and lock with the other globals during a tool.
Double-click resets either slider. Local rows follow Exposure; Reset adjustments
clears them, and single-control history labels name the local and changed control.

Presence (Texture, Clarity, Vibrance, Saturation) stays enabled for every source kind.
Vibrance and Saturation dim on monochrome; Texture and Clarity remain enabled.
The group locks with the global edits while a tool is open.

One completed gesture is one history step. Escape restores an unfinished canvas
gesture, then cancels armed creation, then closes Locals; Undo during a drag cancels
that drag. Navigation and snapshot/replacement commands discard unfinished geometry.
Original, split, fullscreen and preset/history hover suspend editing and visualization
while preserving selection. Presets/paste preserve destination locals; Develop Reset
clears them and Undo can restore them.

+ Brush arms “Paint to create · Escape cancels”; Enter does nothing and Place at
center is absent. The first release creates Brush N with one “Add Brush” history
step. Brush rows use ✎. The Brush section replaces Geometry and Center in view,
including while armed: Paint/Erase, logarithmic Size 1–100 (radius .001–.25 in
corrected-frame long-edge units), Feather 0–100%, Flow 5–100%, and Clear strokes.
Tool preferences persist after a 250 ms debounce in app settings, outside image history; strokes snapshot
them. Clear strokes is one history step and retains the empty local.

A click is a dab; a drag keeps points at least max(.15 radius, one screen pixel)
apart and retains the final point. Shift+click joins the previous stroke's end.
Each release commits “Brush stroke” or “Erase stroke”. Quantized points clamp to
[-1, 2]. Across the document, 96 strokes or 4,000 points refuse another stroke;
a stroke at the point cap stops collecting and releases what it has. The instruction
explains the cap. Escape, Undo, capture loss and navigation discard unfinished paint.
Other locals' pins select before painting; the selected brush's own pin is excluded.
Brush pins anchor to the first point of the first stroke. Pan, zoom and suspended
loupe behavior are unchanged.

Over the image, the brush cursor replaces the system cursor with outer radius and
inner feather circles, CropBorder over CropHandleStroke, plus Paint/Erase indication;
below four screen pixels it becomes a crosshair. Letterbox space restores the cursor.
An immediate LocalMaskColor ribbon (Paint) or translucent white band (Erase) accompanies the
stroke. Preview ticks run at least 60 ms apart with one render in flight. New points
wait for it to paint, then dispatch the latest state; a trailing dispatch is guaranteed.
Completion and discard cancel pending preview work. Show Mask starts on each session
and O toggles it; it is not persisted. Visibility depends only on Show Mask, M held,
a brush armed, a brush stroke in progress or Pick Hue, never brush values. Requested
masks stay pinned during a drag; the release ribbon stays until its committed mask lands.

Paint anchors in the geometry-corrected, pre-crop frame. Crop and quarter-turn rotation
preserve alignment; horizon or keystone edits after painting shift paint relative to
scene content, as they do for gradients.

Show Mask is display-only, including for disabled/neutral locals; it never enters
pixels, scopes, thumbnails or export and only temporarily suppresses clipping display.
Restricted masks use the loaded base matching the accepted surface, without source
reads or decode. A visible mask stays until its content replacement arrives; Updating
mask marks pending content only. Surface-size swaps neither clear nor request masks.
Retention ends on image/local changes, hiding, suspension or rollback.
Pick Hue temporarily shows the mask, commits an accepted sample once, and yields to
Escape, navigation, other tools and canvas gestures before handle hit-testing.

The right pane is mode-differentiated. In Browse it is a **review pane** — the
fixed thumbnail histogram and metadata/EXIF block ending with Location —
with no editing controls. Its one command, **Sync settings…**, is a full-width footer
below the scrolling details, with Develop's action-bar margins; it stays visible in
grid, Loupe and Compare. Everything below is a Develop-only surface (Browse editing
surfaces remain a non-goal, §10).

```
Scope box              (fixed)
[Crop] [Locals]         (fixed tool row)
CROP [Cancel] [Apply] / LOCALS [Show Mask] [Close]  (fixed active-tool header)
Scrolling stack:
Crop                   (only in Crop mode)
  [Horizon]
  [Lock aspect ratio] [Auto] [Reset crop] [instruction]
Locals                 (only in Locals mode)
  [+ Linear] [+ Radial] [+ Brush]
  Empty: [stable-height instruction / armed creation placeholder]
  With locals:
  [local list] [Exposure / Whites / Blacks / Temperature / Tint / Saturation]
  [Reset adjustments] [Center in view]
  [polarity (radial)] [Geometry disclosure] [instruction / armed creation] [divider]
Profile                (always shown; disabled for non-RAW)
  [profile ComboBox, including Choose file…]  status / loading
White Balance
  [mode/preset ComboBox]  [Auto button]  [eyedropper button]
  Kelvin   ────────●────────   5500K
  Tint     ──────●──────────   −12
Adjustments            (no Temperature slider)
  Exposure / Brightness / Contrast / Highlights / Shadows
  Whites / Blacks                                         (−100..100)
  Recovery                                                [Clip | Blend]  (RAW only)
Presence
  Texture / Clarity / Vibrance / Saturation               (−100..100)
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
  [Before/after] [Undo] [Redo] [Copy] [Paste]          Reset
```

The adjustment stack scrolls beneath the fixed scope/tool header and footer.
RENDER.md §9 owns the preview capture-sharpen approximation.

Brightness is disabled (not hidden) at `DisabledOpacity` while a RAW base is active,
because the crossing-on engine has no Brightness parameter; it stays enabled for
JPEG/HEIC/TIFF/proxy sources. The gate follows the loaded base, falls back
provisionally to `ImageFile.IsRaw` before load, survives filmstrip switching, and
never clears the persisted value. Base look remains persisted but has no
panel control; RAW ignores it and standard sources retain it.

Selection clears stale surface facts; only image- and generation-matched outcomes
install atomically. Cached and transient surfaces cannot claim mismatched source
analysis or clipping. RENDER.md §11 owns the outcome contract.

Recovery is a compact, exclusive Clip/Blend control last in Adjustments, enabled
only for RAW sources — provisionally from `ImageFile.IsRaw`, then from the loaded base
fact. The row stays present and dims to `DisabledOpacity` when unavailable, so the
panel does not reflow across mixed-source filmstrips; a contradictory non-RAW loaded
fact disables the row without changing the stored value. Clip is the default.

Color Mixer band selection is session state and untouched
bands remain identity. Detail and Effects apply to all sources. Midpoint dims when
Vignette is zero; Geometry remains image-specific. Optics disables unavailable
corrections in place and distinguishes corrective from aesthetic vignetting.
Selecting an untouched R/G/B curve creates only a draft; committing materializes it.
The embedded curve Reset clears only the selected curve.

When the generation-matched preview or refresh outcome installs
`BaseImageInfo.IsMonochrome`, Develop disables and dims the camera-profile picker,
every white-balance surface and command, Saturation, Vibrance, the color mixer, and
the R/G/B curve selectors. RGB/composite remains enabled. Installation atomically returns an active
color channel to composite, and both the ViewModel and curve control reject later
color-channel selection. Stored color edits remain untouched. The first false-to-true
installation shows one shared transient status message; capability is never inferred
from extension or camera model.

Profile is always shown and disabled for non-RAW. Its ComboBox includes file selection;
Opening the picker refreshes discovery metadata; camera identity drives local discovery;
pending, empty, unavailable and typed rejection states remain visible. Invalid persisted
choices retain their reason while decode uses built-in characterization. No picker
operation hydrates a placeholder. Discovery and resolution are owned by
CHARACTERIZATION.md §7.6.

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

- **Spot pointer:** empty image shows the brush circle; move and angle-based resize cursors emphasize the target circle (unless circles are hidden), persist during capture, and use a crosshair for a Create drag.

- **Display color management:** viewer surfaces derive display copies from retained
  canonical pixels for supported monitor profiles; source reads and edits are unchanged.
  OUTPUT.md §1 owns conversion. About names the profile and whether matrix/TRC, Windows
  ACM, macOS or sRGB fallback owns interpretation; there are no profile controls.
- **Before/after**: original intent resets tone/color while preserving geometry and
  lens corrections so frames stay registered. The toggle changes requested intent
  immediately; visible state follows acceptance. Edits request edited intent, while
  maintenance/cache/resting work preserve the request so late results cannot exit it.
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
- The 1:1 label appears at manual 1:1 and during peek. Develop shows `1:1 · refining`
  until its full-base bitmap is installed, then `1:1`; cloud-only originals show
  `1:1 · preview detail` and never trigger a source read.
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
- Develop's bar uses fixed width tiers (full, no Zoom slider, then a view-actions menu) before sizing its assessment slot; wheel zoom remains available when the slider is hidden.
- The Navigator shows Fit or the slider percentage for a selected photo in Develop and Loupe, including when the slider is hidden.
## 5. Scope box + preview activity

- The fixed scope box selects display histogram, luminance waveform or RAW sensor
  histogram; the histogram plot's geometry, colors and height stay stable. Alternate
  bodies may grow only while selected. Selection is session state; unavailable RAW
  stays disabled with a reason and falls back to display without losing the preference.
  Browse shows only the thumbnail histogram. RGB parade remains deferred.
- A read-only row below either scope shows ISO, focal length, aperture and shutter; it hides when all four values are missing.
- RAW shows sensor channels and clipping percentages, never a display luminance line.
  Display triangles indicate source saturation on the right and finalized floor on
  the left. Missing/stale statistics darken them; unsupported source highlights disable
  only that side. Cached outcomes may supply floor statistics but never RAW/high facts.
- The scope box has no progress surface. Sustained preview preparation uses §9's status
  segment; edits remain enabled and accumulate while the base is acquired.

## 6. Reset / undo / presets / copy-paste scope

Built-in looks change only Contrast, Saturation, Vibrance, the four curves, Color Mixer,
Effects, Texture and Clarity; all corrections survive apply, hover and removal. Below
My Presets, read-only Natural, Portrait, Landscape, Black & White and Creative groups
show approved looks with their intent as tooltips. Empty groups are absent; every
expander remembers its collapse state. Clicking the active look records Preset: None.

- **Reset** returns: `wb → asShot`, `baseLook → null` (source default), all four
  curves to identity (the three optional channel fields to null),
  `hlReconstruction → clip`, `mixer → null`, `detail → source defaults`,
  `effects → null`, manual geometry and locals cleared, lens defaults restored.
  Camera profile returns to built-in; crop/rotation/horizon survive. Reset is one step.
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
- **Batch undo**: Browse grid and Loupe offer **Undo sync (N photos)** / **Undo paste (N photos)**
  beside the status text, independently of transient messages. `Ctrl+Z` runs that action.
  It removes the batch's steps and restores prior documents and history, including any
  truncated redo tail, only for targets still on the unchanged batch step; changed targets
  are skipped. The action ends after use, another sync/paste, folder change, or app exit.
  Results read "Restored N photos" or "Restored N of M · K changed since"; the batch has no redo.
- **User presets** capture color, tonal, color-mixer, all curve, detail, and effects fields and
  still never geometry or camera profiles. Hover, apply, and untoggle preserve the
  current profile.
- **Copy/paste**: Copy (`Ctrl+Shift+C`) snapshots the live document and source filename.
  Browse Paste (`Ctrl+Shift+V`) opens **Paste Settings** for the selection, or the active
  photo when nothing is selected. The dialog offers look groups and a second column for
  Crop & Straighten, Geometry, Camera Profile, Lens Profile, Locals and Spot Removal,
  with in-memory replace counts; All, None and
  Defaults; Cancel and accent Paste. Enter pastes, Escape cancels, and Paste is disabled
  when no group is checked.
  The group-name map is an app setting saved on Paste; missing names use registry defaults
  and unknown names are ignored. Photo-specific groups default off but are remembered.
  Develop `Ctrl+Shift+V` pastes the remembered groups in one step;
  `Ctrl+Alt+Shift+V` or right-clicking Paste opens the dialog. Unticked groups keep live,
  unsaved values. Choosing Crop & Straighten or Geometry discards the crop draft;
  otherwise crop drafts stay live. Quarter-turn rotation never transfers. Locals replace
  the whole list and rebind selection and Show Mask, as history restore does.
  Paste first discards any Spots or Locals gesture and adds one **Paste settings** history
  step. Only choosing every look group carries the source preset marker; partial choices
  clear it. Copy reports "Copied settings from <file>"; paste reports "Pasted settings"
  in Develop (naming replaced photo-specific values) and "Applied to N photos" in Browse.
  Spot Removal replaces the list and rebinds Spots selection; presets exclude repairs and
  Reset clears them. Other bodies keep their own. Skips report "different camera",
  "camera body unknown", "different camera body", "different sensor size" or "facts unavailable".
  Missing source facts and serial are frozen after successful reads on a paste choosing
  Spot Removal when either photo has repairs; failed reads retry on the next paste.
  Header-only serial reads are availability-checked and cached per file for the session.
  Opening the dialog reads no originals. Reframe counts appear there only with cached
  facts; otherwise they appear in the result. Compatibility skips report each group and reason
  until the next status. Camera Profile identity is captured at Copy; paste never reopens it for that group.
  User/Adobe profiles require the same normalized camera model; embedded profiles require the
  same file. Manual lenses require the target’s Lensfun mount match; monochrome targets skip them.
  Built-in and Automatic apply to every RAW without reads. Fully skipped targets keep their
  document and history. Lens drafts survive unless a compatible Lens Profile group is applied.

- **Sync** (`Ctrl+Shift+S`): in the Browse grid or Loupe, transfers the active selected
  photo's settings to the rest of a two-or-more selection through **Sync Settings**.
  The review-pane footer, Browse's **⋯** menu and the thumbnail menu after Write XMP
  sidecars share the command's enablement. Right-clicking a selected photo makes it
  the source and preserves selection. The footer's tooltip stays visible when disabled;
  Compare, Develop, fullscreen and Export cannot sync. Numeric entries
  shadow the shortcut. Sync leaves the source and Copy buffer unchanged.

Recovery has the RAW-only Clip/Blend control and defaults to Clip. Detail fields use
the controls in §2; copy/paste preserves nullable capture-sharpen semantics and both
NR values.

Rotate Left/Right in Develop immediately remaps the last real preview while its replacement
render is pending. This requires known painted rotation/crop/Geometry provenance and identity
Geometry with no crop (or a full crop) in both the painted and current settings; pending Geometry
changes, Original, Before/After split and export proof keep waiting for a real render. The remap
uses the interactive/resting surface, including at 1:1, clears clipping and waveform, and preserves
the histogram. It has no render identity, cache promotion or thumbnail output; a newer real paint,
selection change or disposal releases the retained surface, and failed rotation restores its framing.

## 7. Export workspace

[WORKFLOW.md](../WORKFLOW.md) §6 owns export use; OUTPUT.md §2 owns immutable jobs.

## 8. Keyboard

Slider value fields apply Up/Down (Shift ×10) through ordinary arrow debounce; Tab/Shift+Tab commits immediately and opens the next/previous visible, enabled entry in the nearest scroll host, scrolling into view without wrapping.

Registrations live in [ShortcutCatalog.cs](../../Views/ShortcutCatalog.cs), which
Help & About reads directly. Binding changes update that catalog in the same PR.

## 9. Status bar

Transient hints remain in the existing message area: eyedropper active hint
("Click a neutral area — Esc to cancel") and the rejected-pick message (§4). Persistent
reasons outrank transient hints: source availability, selected RAW decode failure, then
global RAW runtime degradation. Outcomes are correlated to image and preview generation,
so canceled or superseded work cannot pin a stale failure.

One background-activity segment may appear while sustained work is active and is absent
at rest. It summarizes the highest-priority activity with overflow and shows a
determinate bar only for capture-time analysis or export totals; the segment never
animates. Sustained preview preparation uses it exclusively; DESIGN.md owns active-work
indeterminate indicators.

## 10. Explicit UI non-goals

Top-level Develop groups collapse individually, Alt+click solos a group, and
expansion state persists. A collapsed group with saved edits shows a neutral TextSecondary
dot. Locals disclosures and Export options may collapse.
There is no in-app migration/what's-new dialog or Browse editing surface.
Export has no inclusion, rating, filter or range-selection UI.
Exposure remains ±3 EV. The scope-box allowances and histogram-plot freeze are in §5.
