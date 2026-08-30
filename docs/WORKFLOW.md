# The Happy Photon Workflow

Happy Photon is organized around three decisions:

1. **Which photographs are worth keeping?** Use Browse to compare, flag, rate,
   and filter a shoot.
2. **What should each keeper look like?** Use Develop to shape composition,
   light, color, and tone.
3. **What copies do you need?** Select the finished photographs and export the
   required sizes and formats.

This guide takes a new user through those decisions from start to finish. It is
not the only way to use Happy Photon, but it is the workflow the application is
designed to make fast.

## Before you begin

Happy Photon works directly with the photographs in an existing folder. There
is no import step and the original files are not moved.

Edits, flags, ratings, and application settings are stored locally in the Happy
Photon catalog. The default can be changed before creation or in Settings:

```text
~/Pictures/Happy Photon Catalog/
```

Regenerable thumbnails and previews are separate: `%LOCALAPPDATA%\Happy
Photon\cache` on Windows, `~/.cache/happy-photon` on Linux, or
`~/Library/Caches/Happy Photon` on macOS. If this cache disappears, Happy
Photon rebuilds it. Keep the catalog, which contains precious edit state and
presets, in normal backups.

Edits are non-destructive: they are instructions stored in the catalog, not
changes written into the original image. Export creates new files.

On a new installation, first read the Welcome page, confirm where the catalog
and cache will live, and select the Pictures folder that should appear in the
folder tree. Storage is created only when you continue from that step. If the
optional Lightroom step appears, Apply an import or Skip it. Either path keeps
the Pictures choice as the browsing root and advances to an all-set page, where
you can start the guided workspace tour or enter the workspace directly.

Cloud Files providers such as OneDrive are supported without automatically downloading
online-only originals. Happy Photon may show its own cached thumbnail or preview while
the original remains online-only. It reads that original only after a clearly scoped
action such as **Download and open** or a confirmed export.

Happy Photon is pre-1.0 software. Keep a backup of important photographs and
consider learning the workflow with a copied shoot.

Use the focusable **Theme** menu in the title bar to choose **Dark** or
**Middle Gray**. The choice takes effect immediately and is stored with the other
application preferences. Middle Gray provides a neutral L\* 50 surround for judging
photographs; it is a persistent appearance choice, not a temporary color-assessment
mode.

Open **Settings** with the title-bar gear or `Ctrl+,`. The Storage tab reveals
both roots and stages safe moves for the next launch; environment-managed roots
explain why Change and Move are unavailable. The Metadata tab can enable
per-catalog XMP reading or read/write interop for ratings, flags, and
recognized color-label names. Read/write creates or updates a sidecar only
after you change an assessment; enabling it does not publish older catalog
assessments. Happy Photon exchanges only standard Adobe XMP vocabulary (see
`ARCHITECTURE.md` for the exact properties): Lightroom Classic interoperates
fully, while darktable and Bridge versions that recognize a reject only as
`xmp:Rating="-1"` will not show Happy Photon rejects, because the rating keeps
its true star value. Sidecars may sync through the folder's cloud provider,
while the original photo remains untouched and is never downloaded for XMP
work.

Each file's primary version (V1) is the only interpretation that exchanges
assessments with its XMP sidecar. Ratings, flags, and labels on V2–V8 stay in the
Happy Photon catalog.

## Bring assessments from Lightroom Classic

Choose **Import from Lightroom…** in the Folders header's **More folder actions** menu,
or use the optional Lightroom step during first run. When Happy Photon finds a local
`.lrcat`, choose from the detected catalogs or browse for another. Close Lightroom
first. Happy Photon summarizes locations it matches automatically. Map any moved
location you want to import, or leave it blank to skip those photos. Then choose
whether Lightroom replaces differing Happy Photon values or only fills empty values,
review the automatically updated report of what will change, and import. An import
never clears a local rating, flag, or color label, and never writes to Lightroom or an
original photograph.

The preview checks that each mapped photo file exists without opening it. Missing
files are skipped; if none of the mapped photos exist, copy or mount the originals or
correct the mappings before Apply becomes available. Unmapped locations, virtual
copies, and unsupported files are informational skips, and when multiple Lightroom
records map to one destination path the later record is used. “Nothing to import”
means the source catalog has no ratings, flags, or labels; “Nothing matched” means its
source paths need review. Re-running the same import performs no catalog writes when
everything is already up to date.

## 1. Open and survey the shoot

Start in **Browse**. Press `G` at any time to return to it.

The right review pane keeps the active photograph's thumbnail histogram and file,
camera, exposure, and location details together. With two or more photographs
selected, it also shows the selection count, capture-date range, and combined local
file size. Online-only originals remain excluded from those aggregates until they are
downloaded.

Hover the filename to see its containing folder. Right-click the metadata panel and
choose **Copy details** to copy the visible rows as plain text. When coordinates are
available, click them to open that position in OpenStreetMap. This explicit click is
the only map action; the review pane never contacts a map service in the background.
An altitude can still appear without coordinates, but it is not a map link. A muted
date is the file-modified fallback used only for display, not for burst grouping or
selection capture-date ranges.

1. Choose a folder in the folder tree on the left.
2. Click the folder or press `Enter` to move focus to the image grid.
3. Let the first thumbnails appear, then move through the shoot with the arrow
   keys or by clicking thumbnails.
4. Turn on burst grouping with the stacked-frames icon when the folder contains
   sequences of closely spaced frames.

Use the three thumbnail buttons at the right edge of the Browse footer to change
browsing density. **Small** shows the most photographs, **Medium** is the default, and
**Large** provides a sharper comparison view. The choice is remembered across launches.
When Large needs a better cached image, Happy Photon keeps the existing thumbnail
visible while it upgrades locally available sources in the background. It never
downloads an online-only original for that quality upgrade.

A `!` marker identifies a thumbnail that could not be loaded or a RAW whose Develop
decode failed, even when an older embedded or cached image remains visible. A broken
native RAW installation is shown once as a global degraded status rather than marking
every RAW tile; reinstall Happy Photon to repair it.

If photographs are added, removed, or renamed outside Happy Photon, use the
**Refresh folder** button beside **More folder actions** in the Folders header. Refresh
re-reads the currently viewed folder and its immediate subfolder list while
preserving active filters and cataloged edits, ratings, and flags for paths that
still exist.

Right-click a Browse thumbnail to **Copy path**, **Reveal in File Explorer**, or
manage its Versions. **New Version from Current** copies the active interpretation's
settings into a sibling tile, up to eight versions. **Rename version label…** sets a
short optional badge label; blank labels display as `V<n>`. **Delete version** confirms,
then removes only V2–V8 catalog state and cache assets, never the original file. Right-clicking a
photograph outside the current selection makes it the
selection; right-clicking one already selected preserves the selection. Copy path
places the selected photographs' full paths on the clipboard in grid order, one per
line. Reveal selects the active file in Explorer or Finder; on Linux it opens the
containing folder. The folder tree's right-click menu offers Reveal only.

Delete and the `Delete` key use the same targets as other Browse actions: the grid
selection when it is non-empty, otherwise the active photograph. After one confirmation,
Happy Photon deletes selected V2–V8 interpretations from the catalog without affecting
their original files, and moves selected primary originals and their resolved XMP
sidecars to the system Trash. A failed file does not stop the rest
of a batch; the final dialog names every failure or skipped sidecar. Online-only files
and sidecars are never downloaded for deletion. Network locations and removable media
are refused because their deletes may not be recoverable. On Windows, a fixed drive
whose Recycle Bin was explicitly disabled remains a known limitation: Windows may
delete permanently just as Explorer does; closing that case requires a future
`IFileOperation` implementation with recycle-on-delete enforcement.
Deleting a file removes all of its versions from the catalog and grid.

Burst grouping places photographs captured within two seconds into the same
sequence. It does not choose a winner; it makes neighboring frames easier to
recognize and compare. Happy Photon analyzes capture times only after Bursts is
enabled; local photographs are analyzed and online-only photographs are reported
as skipped. Sustained analysis appears in the shared background-activity segment
with processed and total counts; it is absent again after the sweep finishes or
Bursts is turned off.

The **J+R** footer toggle starts off, showing RAW and JPEG files separately. Turn it on
to combine same-folder, same-name files into one JPEG tile; the choice is remembered.
Rating, flagging, or labeling that tile assesses both primary files. In Develop, press
`Shift+R` or use **J|R** beside Before/After to switch instantly between the camera JPEG and
RAW while keeping the zoomed viewport. The switch changes files, so it clears undo
history; moving to another capture returns to its JPEG. Turn pairing off to browse,
assess, or export the physical files separately.

Online-only photographs stay visible with a cloud badge or placeholder. The folder
status reports how many will not be downloaded automatically. To work with one
cloud-only image, select it and choose **Download and open**; this downloads only
that original.

Do not start adjusting every image yet. The first goal is to understand the
shoot and remove obvious misses from consideration.

![Happy Photon Browse showing the folder tree, filters, thumbnail grid, and
assessment controls](screenshots/Screenshot_Browse.png)

## 2. Cull before you develop

Flags answer **what should happen to this frame?**

- Press `P` to set **Picked**.
- Press `X` to set **Rejected**.
- Press `U` to return photographs to **Unflagged**.
- Press `` ` `` to toggle **Picked** and **Unflagged**.

In Browse, these commands affect the selection when it is non-empty, even when the
active photograph is outside it. With an empty selection they affect the active
photograph. In Develop, Browse Loupe, and Compare they affect only the active
photograph. Pick and Reject are set-only; the backtick toggle clears a uniformly
Picked target or sets Picked otherwise.

Develop and Browse Loupe confirm each flag, rating, and color-label change briefly over the
photograph — "Set flag: Picked", "Unset rating: ★★★" — and shows nothing between
changes. Browse remains where a photograph's current assessment is on display.

Unflagged is useful for undecided frames. Rejected does not delete a file, and
Picked does not automatically select it for export.

Move quickly on the first pass:

1. Reject clear misses such as accidental frames or unusable expressions.
2. Pick the strongest frame from each moment or burst.
3. Leave uncertain comparisons unflagged and revisit them later.

The Pick, Reject, and Unflag buttons beneath the image grid provide the same actions.
Pick and Reject always set their flag; Unflag always clears either flag, including
across a mixed selection.

### Add ratings only when they help

Stars answer a different question: **how strong or important is this frame?**
Press `1` through `5` to set a rating; repeating a star value clears it when every
target already has that rating. Press `0` to clear it directly.

Ratings use the same targets as flags.

A simple starting method is to flag first, then rate only the picked images:

- `3` — a solid keeper
- `4` — a standout from the shoot
- `5` — one of the very best

The meaning is yours to define. Consistency is more useful than a complicated
rating system.

### Add color labels for another classification

Color labels provide a third, independent assessment axis. Use the assessment swatches
or press `6` through `9` for red, yellow, green, or blue; clicking or pressing the
active color clears it. Label commands use the same targets as flags and ratings. The
swatch ring always describes the active photograph, not the selected target set.

### Filter the result

The Browse filter bar uses labeled groups of compact controls that can be
combined to show:

- RAW or JPEG files;
- picked or rejected images;
- a minimum rating chosen from the five-star strip;
- no label or one named color.

For example, choose **Picked** and click the third star to review
photographs rated three stars or more that are most likely to be delivered.
Re-click any active file type, flag, threshold star, or color swatch to clear that
filter group. If nothing matches, the grid says so in place and offers a **Clear**
action that resets every group at once. Changing a flag or rating while a filter is
active can make the current image disappear when it no longer matches; Happy Photon
advances to another visible image.

**Delete Rejected** is a separate, destructive cleanup action. After
confirmation, it moves every rejected image in the open folder to the operating
system Trash. Rejecting alone never moves or deletes the original.

## 3. Develop the keepers

Double-click a thumbnail or press `D` to enter **Develop**. Use the previous and next
buttons below the image, or the left and right arrow keys, to move between visible
images without returning to Browse.

When the rendered cache matches the current settings, the photograph, display
histogram, waveform, and display-floor clipping become useful together before the
original is decoded. If fresh preparation lasts beyond the normal delay, the shared
status bar says **Preparing preview** until the first coherent fresh render settles;
there is no separate progress line under the scopes. Returning to Browse and then
Develop on the same active photograph reuses the current in-memory preview pair.

If an original is online-only, an existing cached preview can still appear, but Happy
Photon does not start a fresh decode until you choose **Download and open**.
If LibRaw cannot decode a locally available file, an actionable message remains pinned
for that photograph (including common unsupported variants such as Nikon HE), and the
Browse keeps its failure marker when you return. A successful retry clears both. Source
availability messages take priority over file decode and global runtime messages.

A useful editing order is composition, light, color, and then refinement. You
do not have to touch every control.

Use **Assess** beside **Fit**, or press `L`, to judge the photograph against
an invariant white reference band and mid-gray surround. The session-only mode
works in Develop and fullscreen, re-fits when toggled so the complete reference
field is visible, and never changes edits or exported pixels.

Below 1:1, the magnifier cursor marks where you can press and hold the left mouse
button over the Develop, Browse Loupe, fullscreen, or Compare image to peek at 1:1 under the pointer.
Drag to pan while peeking, then release or press `Escape` to return to the
unchanged zoom and view.

![Happy Photon Develop showing presets, the image viewer, histogram, and
adjustment controls](screenshots/Screenshot_Develop.png)

### Set the composition

Use the controls below the image to:

- rotate in 90-degree steps;
- enter crop mode with `R`;
- straighten the horizon within the crop controls;
- lock the current crop aspect ratio when needed.

Apply the crop with **Apply** or `Enter`. Use **Cancel** or `Escape` to abandon
the current crop operation. Geometry belongs to the individual frame, so crop,
rotation, and horizon settings are never transferred by presets or copy/paste.

### Shape the light

Start with the largest problem and make the smallest adjustment that solves it:

- **Exposure** changes the overall light level in photographic stops.
- On RAW files, **Contrast**, **Highlights**, and **Shadows** shape the AgX tone
  engine around fixed scene middle grey: slope, shoulder, and toe respectively.
- On JPEG/HEIC/TIFF/proxy sources, those same controls retain their familiar
  display-referred behavior.
- On RAW files, **Recovery** defaults to **Clip**. Choose **Blend** to blend
  channel-clipped highlight information during RAW decoding. The row remains in place
  but is disabled for standard sources. The current preview remains visible while the
  updated decode completes in the background.
- **Brightness** is available for standard sources. It is disabled for RAW because
  the crossing-on engine anchors global light with Exposure; switching sources does
  not erase a stored Brightness value.

Watch the photograph first and use the histogram as supporting information.
Avoid correcting the histogram merely to make it fill the graph.

The display histogram's right triangle reports source saturation: exact sensor maximum
for RAW, or encoded near-white samples for JPEG/HEIC. TIFF, PNG, and other formats show
that side as unavailable. The left triangle reports pixels at the finalized display
floor. Hover an available triangle to peek that side over the photograph. Click either
triangle or press `J` in Develop to latch the clipping overlay. Red stays fixed across tonal and color edits
apart from geometry; blue responds as edits change the rendered output.

### Shape the color

- **Kelvin** moves the white balance toward cooler or warmer color.
- **Vibrance** changes lower-intensity colors most while protecting already-saturated
  colors and common skin hues.
- **Saturation** scales every color's perceptual intensity uniformly; −100 is grayscale.
- **Color Mixer** targets Red, Orange, Yellow, Green, Aqua, Blue, Purple, or Magenta.
  Pick a swatch, then use Hue to steer that band toward its neighbors, Saturation to
  change only its color intensity, and Luminance to lighten or darken it. A dot marks
  every touched band; double-click resets one slider, while the Develop footer RESET
  clears all bands with the other color and tonal adjustments.

Kelvin usually answers whether the photograph feels too cool or too warm.
Vibrance and saturation answer whether the color feels too weak or too intense.
If the image already looks right, leave them alone.

For a true monochrome RAW, the camera profile, white balance, Saturation, Vibrance,
color mixer, and R/G/B channel-curve controls stay visible but disabled. Existing saved color
values are preserved for later color sources; Exposure, the composite curve, tone,
detail, effects, scopes, and export continue to work normally.

### Refine the tone

Use the **Tone Curve** when the basic controls cannot produce the tonal shape
you want. It is a finishing tool, not a required step. RGB shapes the composite
curve. Choose R, G, or B for channel-specific balance and split-tone effects; edited
channel letters stay tinted. The curve's Reset clears only the active channel, while
the Develop footer RESET clears every curve and other tonal adjustments.

### Refine detail

Use **Luma NR** for luma grain, **Sharpen** for capture detail, and
**Chroma NR** for color speckling. All three work on RAW, JPEG, HEIC, and TIFF. Noise
reduction runs after tone, so revisit it after a large shadow or exposure change. The
Develop viewer uses a bounded 1600px preview base even at 1:1, so judge capture
sharpening and subtle noise reduction on an export-scale render.

### Add finishing effects

Use **Vignette** after composition to darken negative-value corners or lift
positive-value corners around the finished frame. **Midpoint** moves the falloff onset
and is dimmed until Vignette is active. **Grain** adds deterministic monochrome film
grain; choose Fine, Med, or Coarse for its output-pixel size. Both effects apply to RAW
and standard sources. During crop mode the vignette is temporarily centered on the full
canvas used by the crop overlay, then recenters when the crop is applied.

In Develop or fullscreen, press `\` to toggle between the edited image and the
original. In Develop, choose **Y|Y** or press `Y` to compare that original beside the
live edited image; fit, zoom, pan, and the press-and-hold loupe stay synchronized.
Re-click **Y|Y** or press `Escape` to leave the split. The History panel above Presets
lists committed edits newest-first; click a step to return to it, or use `Ctrl+Z` to
move back and `Ctrl+Y` or `Ctrl+Shift+Z` to move forward. A new edit after moving back
discards the later steps; to discard them immediately, right-click the target step and
choose **Clear History Above This Step**, or Alt-click it. These editing shortcuts do
nothing in Browse. The reset
button clears the color and tonal adjustments while preserving crop, rotation,
and horizon settings; reset those separately in the geometry controls.

Edits are saved to the catalog automatically. Export is not required to
preserve the edit instructions.

## 4. Keep a series coherent

Photographs from the same light and location often benefit from the same color
and tonal starting point.

### Use a personal preset

When the current image has a useful look:

1. Choose **Save Current** in the presets panel.
2. Give the preset a descriptive name.
3. Hover over the preset to preview it on another image.
4. Click it to apply it.

Applying a preset replaces the image's current color, mixer, tonal, detail, and effects
settings. Click
the active preset again to remove it and reset those settings. Presets never
include crop, rotation, or horizon changes.

### Copy edits between images

Use the copy and paste buttons in the Develop footer, or press `Ctrl+Shift+C` and
`Ctrl+Shift+V`, to transfer the current image's color, mixer, tonal, composite and
channel curve, detail, effects, and preset settings to another image.

To apply the settings to several photographs:

1. Return to Browse with `G`.
2. Select the target images.
3. Press `Ctrl+Shift+V`.
4. Review and confirm the batch operation.

Batch paste adds a **Paste settings** step to every target, so it can be undone when
that photograph is opened in Develop. Crop, rotation, and horizon settings on every
target remain unchanged.

After sharing a starting point, inspect the images individually. Exposure and
temperature can still vary within a series.

## 5. Build a selection

Selection is a working set shared by Browse assessment actions, batch paste, and
export:

- **Picked** means the image passed your cull.
- **Rating** records its relative strength or importance.
- **Selected** means include this image in the next Browse action or export.

Filter the Browse to the group you want before selecting it. Then:

- click a thumbnail's check badge, or press `Ctrl+Space`, to toggle the current image;
- use `Ctrl+Click` to add or remove individual images;
- use `Shift+Click` to select a range;
- press `Ctrl+A` to select every image currently visible through the filters;
- press `Ctrl+D`, or choose **Deselect All** from **More browse actions**, to clear
  the visible selection.

A plain click or arrow-key move replaces the selection with the newly focused
photo, so single-photo assessment always lands on the photo under the focus ring;
use the modifiers above to build a multi-photo selection.

Images that become hidden by a new filter are removed from the selection. Set the
filters first, then make the final selection.

Use the fullscreen button below the Develop image, or press `F`, with two or more
photos selected to review only that selection in fullscreen, starting from the first
selected photo in the Browse's current order.
Navigation stops at the first and last selected photo, and the `SELECTION` badge shows
the current position. The set updates with visible selection changes; if fewer than
two selected photos remain, navigation returns to the full folder until full screen is
entered again. With zero or one photo selected, full-screen navigation covers the full
folder as usual.

Press `E`, `Enter`, or `Space` in the Browse grid, or choose the **E** footer toggle,
to open the active photograph in Browse Loupe. The folder tree, review pane, and
assessment footer remain available while the grid becomes one large image. Arrow keys
move through the current 2+ photo selection without changing it, or through visible
photos when the selection has fewer members. `Space` or `Z` toggles Fit and 1:1;
`E`, `G`, or `Escape` returns to the same active grid tile, `D` enters Develop, and `F`
enters fullscreen.

With two to four photos selected, choose the X|Y toggle in the Browse footer before
the burst and thumbnail-size controls. Two photos appear side by side; three or four use
a 2×2 view. Click a
pane or use the left and right arrows to choose the active photo, then use the usual
flag, rating, and color-label controls. Fit, zoom, pan, and the press-and-hold loupe stay
synchronized across every pane. Re-click the checked X|Y toggle or press `Escape` to return with
the comparison selection and active photo preserved. Press `C` from Browse Loupe to
enter the same comparison directly; with fewer than two selected photos it does nothing.

## 6. Export finished copies

Choose the **Export** workspace to prepare finished copies. Its left filmstrip takes a
snapshot of the Browse selection; uncheck a capture to exclude it from this batch
without changing the Browse selection. The center shows the standard preview
immediately. Turn on **Proof** when you need to check the current photograph through
the armed recipe's color space, output sharpening, and size; the preview stays visible
while the full proof renders, then swaps when it is ready. The status line says whether
the displayed pixels are `PREVIEW` or `PROOF`, followed by the live format and color
space and, for a sized recipe, its pixel cap. Turn Proof off to return to the preview.
On the right, arm any combination of the fixed **Hi-Res**, **Web**, and **Small**
recipes and set their shared format, quality, color space, sharpening, naming, location
metadata, and destination controls. The count line shows captures × armed recipes,
including zero recipes.

Versions export as independent interpretations. Exporting either version by itself
keeps the ordinary name; when one job includes multiple versions of the same file,
their outputs gain stable `-V<n>` suffixes and the report identifies both file and version.

Press `Ctrl+Shift+E` from Browse or Develop to enter Export with the current selection armed.
Press `Enter` or choose **Export** to run the capture × recipe job. `Escape` returns to
the workspace you came from; it does not stop a run already in progress.

1. Choose an output folder. The default is an `export` folder beneath the open
   photo folder.
2. Choose JPEG, PNG, WebP, or 16-bit TIFF. TIFF is lossless, uses ZIP compression,
   and is intended for a high-precision handoff to another editor.
3. Choose **sRGB** for the broadly compatible default, or **Display P3** when the delivery
   software and display are color-managed and a wider gamut is useful. Preview remains
   sRGB; the embedded export profile lets color-managed software reproduce the same color.
4. Set the quality when the selected format uses it. The quality control remains visible
   but disabled for lossless PNG and TIFF.
5. Choose one size:
   - **Hi-Res** applies no output-size limit.
   - **Web** constrains the longest dimension to the specified size.
   - **Small** creates a smaller longest-dimension copy.
6. Choose **Off**, **Screen**, or **Print** output sharpening. Screen preserves the
   delivery default; Print is stronger, size-aware, and can sharpen Hi-Res output.
7. Choose a naming pattern and check the filename preview.
8. Start the export. Its queue appears above the footer and continues if you switch
   workspaces.

If the selection includes online-only originals, Happy Photon first reports their exact
count and approximate logical size. Choose **Cancel** to leave them untouched or
**Download / Export** to approve downloads for that selected batch. Stopping an export
after approval is best effort because the cloud provider may already have started a
download.

Before work starts, Happy Photon refuses targets matching loaded originals or another
target in the same job. Existing output files are confirmed together. The exported
files then go directly into the chosen output folder; a file that appears after the
confirmation pass is not overwritten.

Export decodes and edits each photograph, then creates new output files.
Targets that would overwrite a loaded original are refused.
When some targets fail, the Export card lists the failed capture-recipe pairs alongside
any warnings and offers **Retry failed only** without rerunning successful siblings.

## A complete first workflow

For a first shoot, keep the process deliberately simple:

1. Open the folder and enable the stacked-frames burst control if it contains
   rapid sequences.
2. Make one quick pass with `P`, `X`, and `U`.
3. Filter with **Picked** and give only the strongest images three to
   five stars.
4. Keep the **Picked** filter active and click the third threshold star.
5. Develop one representative photograph.
6. Save its look as a preset or copy its settings to similar photographs.
7. Review every edited image and correct it individually.
8. Return to Browse, keep the final filter active, and press `Ctrl+A`.
9. Export the preferred delivery size.

The result is a small, coherent set of finished copies while every original
remains where it started and unchanged.

## Essential shortcuts

| Key | Action |
| --- | --- |
| `G` | Switch to Browse |
| `D` | Switch to Develop |
| `E` / `Enter` / `Space` | Open Browse Loupe from the grid |
| Arrow keys | Move between images |
| `P` / `X` / `U` | Pick, reject, or unflag |
| `` ` `` | Toggle Picked / Unflagged |
| `1`–`5` / `0` | Set or clear a rating |
| `6`–`9` | Set red, yellow, green, or blue color label |
| `Ctrl+Space` | Toggle the active photo in the selection |
| `Ctrl+A` / `Ctrl+D` | Select or deselect all visible images |
| `Ctrl+'` | Create a version from the current interpretation in Browse or Develop |
| `C` | Compare 2–4 selected photos |
| `R` | Toggle crop mode in Develop |
| `\` | Toggle before/after in Develop or fullscreen |
| `Y` | Show Before and After side by side in Develop |
| `Shift+R` | Switch between a paired JPEG and RAW in Develop |
| `L` | Toggle color assessment mode in Develop or fullscreen |
| `Space` / `Z` | Toggle Fit and 1:1 in Develop or Browse Loupe |
| `J` | Toggle clipping overlay in Develop |
| `Ctrl+Shift+C` / `Ctrl+Shift+V` | Copy or paste edit settings |
| `Ctrl+Z` / `Ctrl+Y` | Move backward or forward through Develop history |
| `Ctrl+Shift+E` | Open the Export workspace |
| `Enter` | Run Export, apply crop, or move from Browse Loupe to Develop |
| `Ctrl+,` | Open Settings |
| `F` | Toggle image-only fullscreen |
| `Escape` | Exit Browse Loupe or Compare, cancel crop, or return from a transient view |

### Gesture map

The review gestures deliberately keep distinct scopes: `\` shows before/after in
Develop or fullscreen, `L` toggles the Develop assessment surround, `R` owns crop
in Develop, `Y` opens the synchronized Before | After split, `Shift+R` switches a paired
capture's representation, `C` opens Compare from Browse, `J` owns clipping in Develop,
and holding the left mouse button invokes the loupe
below 1:1 in Develop, Browse Loupe, fullscreen, or Compare. The visible E and X|Y
toggles teach the Loupe and 2–4-photo Compare entries. Future version gestures
must extend this same catalog without colliding with these keys.

Use the `?` button in the title bar to open **Help & About**. The complete
shortcut reference is selected by default, with build and project information
available on the About tab. Update discovery is manual-only: Happy Photon makes
no automatic update network requests, and About contacts GitHub only when you
choose **Check for updates**. Store-packaged Windows installations open the
Microsoft Store, which manages their updates; other installations open the
matching GitHub release. A muted dot on `?` means an in-session manual check
found a newer release, and Help then opens on About.
