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
advances to an all-set page; cancel stays in the wizard.
Start browsing finishes setup and focuses the folder tree.
ARCHITECTURE.md owns the startup gate and atomic completion checkpoint.

Browse, Develop and Export each show a corner tips card on first entry.
Got it dismisses that mode's card permanently; Keyboard shortcuts opens Help on its shortcuts tab.
Tips never take focus and stay hidden outside the main surfaces, during setup and in fullscreen.

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
Restore from backup… in Storage stages a restore for the next launch, with Cancel restore.

About owns manual update checks and muted inline results. An available release adds
a muted dot to Help; opening Help then selects About and offers the channel-appropriate
Store or GitHub action. There are no automatic update requests.

## Colors

- Control chrome uses achromatic hover, selection, active and focus states.
- Browse shows selection by tile tone alone (unselected < hover < selected, at least ΔL\* 8
  apart in both themes) and the active photo by the ring; tiles carry no check badge.
- The brand colour is reserved for the icon and welcome heading.
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
are strictly achromatic, including the active-image ring and selection tone, so they
introduce no color cast beside photographs. Semantic colors retain their meanings.

| Token | Contract |
|---|---|
| `ViewerSurround` | Theme-specific photograph surround |
| `ControlHover`, `ControlSelected`, `ControlActive`, `OnControlActive` | Neutral interaction states |
| `SystemAccentColor*` | Achromatic Fluent control ramp |
| `Brand`, `BrandMark` | Brand identity, separate from control accents |
| `AssessmentGray`, `AssessmentWhite` | Invariant assessment references; never aliases of theme surround |

## Typography

Sora supplies headings; Hanken Grotesk supplies body and control chrome. Panel headers
use mixed-case Hanken Grotesk SemiBold, muted and without tracking. JetBrains Mono is
reserved for numeric readouts, slider values, dimensions and keyboard hints.
Preset subgroup headers step down to body size (11), Medium, TextMuted.
The type scale is Title 20, Heading 15, Label 12, Body 11 and Small 10;
`FontSizeSmall` is the minimum. Use mixed case and no tracking anywhere.
The welcome surface uses the named `FontSizeHero` token.

## Layout & Spacing

The UI is flat and square; only semantic circles stay round.
An instrument at the top of a side pane (Navigator, scope) is a `SurfaceHigh` band with a 24 px
header row, 4 px padding above it and a `Divider` hairline below, holding its content in a `SurfaceLow`
well; what follows starts one 8 px step below the band.

Panes are mode-specific: Browse owns review; Develop owns editing controls
(pipeline/UI.md §2). Export layout and behavior live in WORKFLOW.md §6.
Workspace scrollbars are hidden by default. A list that must show more content uses a very
thin, thumb-only bar with no arrow buttons, never Fluent's default wide scrollbar:
`overlay-scrollbar` floats over the rows and fades in on hover, for plain lists (folder
tree, History); `thin-scrollbar` stays visible in its own gutter, for lists whose rows
hold controls (Masks), so it never covers them.

The navigator retains the active thumbnail and online-only action so folders retain
space. When zoomed, it outlines the visible image region with a primary-text hairline
and dark halo, mapped to the image rather than its gutters. The outline is informational;
it tracks pan/zoom and disappears when effectively all of the image is visible, except
during the transient loupe peek.

Browse and Develop share one control-bar layout: navigation (and Develop rotation) left,
assessments in the middle, mode commands right. The fixed-width assessment cluster centres
on the bar, shifting only as far as needed to clear the side groups; it becomes compact
read-only, then empty when space runs out. Develop hides its zoom slider before shifting
the cluster; Browse's online-only message yields first. Assessment state never moves the
cluster or its neighbours. Key letters appear in tooltips and Help, not in the cluster.
Bar tooltips open directly above their control, so they never cover the pointer.
Develop and Loupe shortcuts briefly show a chrome-less confirmation over the
photo, with primary text and a dark halo for legibility on unknown content.

Fullscreen exposes a muted exit chip on pointer movement, then fades it away. It is
clickable only while visible, so exiting is not keyboard-only. Clipping overlays follow
pipeline/UI.md §4; image viewing aids never become exported pixels.

Review metadata groups File, Camera and Location. Missing rows remain absent. A muted
file-modified fallback does not become capture time; exposure-bias, focal-equivalence
and crop-factor details stay with exposure/lens information. A conditions line appears
only for deviations such as flash, non-pattern metering or manual white balance.

## Components

Icon buttons use drawn paths at 14px (24px button) and 12px (20px button); text buttons are 28px or 24px high.

Color labels occupy fixed red, yellow, green, blue and purple slots. Caption markers
align below EDIT so the photograph stays unobstructed. Assessment/filter swatches come
from the append-only enum; tooltips name them and re-clicking clears the active filter.

Dialog actions stay bottom-right, with Close/Cancel immediately left of the primary.
Dismiss buttons never move when the primary appears or disappears.

Indeterminate indicators run only during represented work (startup initialization or
first-run busy), never at rest or while hidden. Sustained preview preparation uses the
static status-bar activity segment (pipeline/UI.md §9).
