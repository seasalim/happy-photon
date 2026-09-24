# Happy Photon Design

The desktop workspace uses restrained, monochrome chrome so photographs and
color-semantic data carry the visual emphasis. UI resources live in
`Themes/HappyPhotonTheme.axaml`; code-drawn overlays use `Views/HappyPhotonColors.cs`.

## First run

New installations use Welcome, Storage, and Pictures in sequence. Welcome explains
the local catalog and untouched originals. Storage shows catalog/cache defaults and
separate Change pickers, with the uninstall warning appropriate to the chosen location.
Confirming Storage is the catalog-creation boundary; empty configured roots stay
read-only until then. Pictures chooses the top-level browsing folder.

A bounded shallow local probe may offer a Lightroom step. Apply or explicit Skip
advances to an all-set page; cancel stays in the wizard. Start tour and Skip both
finish setup and focus the folder tree; only Start tour opens the guided workflow.
ARCHITECTURE.md owns the startup gate and atomic completion checkpoint.

The session-only tour uses non-modal coachmarks anchored to stable Browse/Develop
layout points. It suspends when its view is left and resumes on return. Unrelated
sections dim while the target stays interactive; the Browse empty card stays hidden.
Decorative photon trails never intercept input. Tour navigation never changes photo
state, filters or selection; its Export entry offers a return to Browse when empty.

## Import from Lightroom Classic

First run and the Folders header expose the same import dialog. It summarizes local
roots, offers mappings for unavailable roots, and previews Lightroom-wins or fill-empty
policies before Apply. Missing photos are skipped; no matched photos means Apply is
disabled. Reports distinguish no assessments from no matching paths and keep expected
skips separate. Import updates existing Browse objects without reloading thumbnails;
first-run mappings never replace the Pictures choice as the browsing root.
The workflow is described in WORKFLOW.md; ARCHITECTURE.md owns import safety.

## Settings

The title-bar gear sits between Theme and Help. Settings shares Help & About's tab
and footer structure. General contains theme; Storage reveals roots and stages
restart-time moves; Metadata applies catalog-scoped XMP settings immediately.

About owns manual update checks and muted inline results. An available release adds
a muted dot to Help; opening Help then selects About and offers the channel-appropriate
Store or GitHub action. There are no automatic update requests.

## Colors

- Control chrome uses achromatic hover, selection, active and focus states.
- Brand cyan is reserved for the title-bar mark/wordmark and welcome heading.
- Semantic color identifies bursts, color labels, mixer bands, white-balance tracks,
  clipping/scope channels, and errors/destructive actions.
- Reject uses an invariant near-black surface, light glyph and hairline.
- Kelvin and tint tracks use functional cyan→green→yellow and green→magenta gradients.

### Application themes

Dark is the default; Middle Gray uses the internal identifier `MidGray`.
Middle Gray's photograph surround is the nearest integer sRGB encoding of CIE L\* 50,
`#777777`, about 18.4% relative luminance. This is a display reference, not a physical
18% reflectance card whose appearance would depend on illumination.

Middle Gray remains a dark-family theme with light text on darker chrome. Its neutrals
are strictly achromatic, including the active-image ring and selection mark, so they
introduce no color cast beside photographs. Semantic colors retain their meanings.

| Token | Contract |
|---|---|
| `ViewerSurround` | Theme-specific photograph surround |
| `ControlHover`, `ControlSelected`, `ControlActive`, `OnControlActive` | Neutral interaction states |
| `SystemAccentColor*` | Achromatic Fluent control ramp |
| `BrandCyan`, `BrandMark` | Brand identity, separate from control accents |
| `AssessmentGray`, `AssessmentWhite` | Invariant assessment references; never aliases of theme surround |

## Typography

Sora supplies headings; Hanken Grotesk supplies body and control chrome. Panel headers
use mixed-case Hanken Grotesk SemiBold, muted and without tracking. JetBrains Mono is
reserved for numeric readouts, slider values, dimensions and keyboard hints.
The welcome surface uses named `FontSizeHero` and `FontSizeFeature` tokens.

## Layout & Spacing

Panes are mode-specific: Browse owns review; Develop owns editing controls
(pipeline/UI.md §2). Export layout and behavior live in WORKFLOW.md §6.
Workspace scrollbars are hidden except for the folder tree and History overlays.

The navigator retains the active thumbnail and online-only action so folders retain
space. When zoomed, it outlines the visible image region with a primary-text hairline
and dark halo, mapped to the image rather than its gutters. The outline is informational;
it tracks pan/zoom and disappears when effectively all of the image is visible, except
during the transient loupe peek.

Browse anchors culling actions left and view/thumbnail state right. Develop anchors
navigation/rotation left and zoom/view state right. Persistent assessments stay in
Browse; Develop and Loupe shortcuts briefly show a chrome-less confirmation over the
photo, with primary text and a dark halo for legibility on unknown content.

Fullscreen exposes a muted exit chip on pointer movement, then fades it away. It is
clickable only while visible, so exiting is not keyboard-only. Clipping overlays follow
pipeline/UI.md §4; image viewing aids never become exported pixels.

Review metadata groups FILE, CAMERA and LOCATION. Missing rows remain absent. A muted
file-modified fallback does not become capture time; exposure-bias, focal-equivalence
and crop-factor details stay with exposure/lens information. A conditions line appears
only for deviations such as flash, non-pattern metering or manual white balance.

## Components

Color labels occupy fixed red, yellow, green, blue and purple slots. Caption markers
align below EDIT so the photograph stays unobstructed. Assessment/filter swatches come
from the append-only enum; tooltips name them and re-clicking clears the active filter.

Dialog actions stay bottom-right, with Close/Cancel immediately left of the primary.
Dismiss buttons never move when the primary appears or disappears.

Indeterminate indicators run only during represented work (startup initialization or
first-run busy), never at rest or while hidden. Sustained preview preparation uses the
static status-bar activity segment (pipeline/UI.md §9).
