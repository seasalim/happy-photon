# The Happy Photon Workflow

## Bring assessments from Lightroom Classic

Choose **Import from Lightroom…** in the Folders header's **More folder actions** menu,
or use the optional Lightroom step during first run. When Happy Photon finds a local
`.lrcat`, choose from the detected catalogs or browse for another; when only Lightroom
Classic is detected, choose a catalog. Close Lightroom first.
Happy Photon summarizes locations it matches
automatically. Map any moved location you want to import, or leave it blank to skip those
photos. Then choose whether Lightroom replaces differing Happy Photon values or only fills
empty values, review the automatically updated report of what will change, and import.
An import never clears a local rating, flag, or color label, and never writes to
Lightroom or an original photograph.

The preview checks that each mapped photo file exists without opening it. Missing files
are skipped. If none of the mapped photos exist, copy or mount the originals or correct
the mappings before Apply becomes available.

The report reserves action-needed messages for an import where none of the assessed
photos matched. Unmapped locations are informational skips, like virtual copies and
unsupported files. It also warns when multiple Lightroom records map to the same
destination path; the later record is used. “Nothing to import” means the source
catalog has no ratings, flags, or labels; “Nothing matched” means its source paths need
review. Re-running the same import performs no catalog writes when everything is already
up to date.

On a new installation, first read the Welcome page, confirm where the catalog and cache
will live, and select the Pictures folder that should appear in the folder tree. Storage
is created only when you continue from that step. If the optional Lightroom step appears,
Apply an import or Skip it; canceling the picker or dialog returns to the step. Either
successful path keeps the Pictures choice as the browsing root and advances to an
all-set page, where you can start the guided workspace tour or skip it and enter the
workspace directly. Lightroom import remains available later from the Folders header's
**More folder actions** menu.

Happy Photon is organized around three decisions:

1. **Which photographs are worth keeping?** Use Library to compare, flag, rate,
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
explain why Change and Move are unavailable. The Metadata tab can
enable per-catalog XMP reading or read/write interop for ratings, flags, and
recognized color-label names. Read/write creates or updates a sidecar only
after you change an assessment; enabling it does not publish older catalog
assessments. Happy Photon writes standard Adobe XMP vocabulary: true stars stay
in `xmp:Rating`, flags use Lightroom-compatible `xmpDM:pick`, and color labels
use `xmp:Label`. Reads likewise use only these standard XMP properties.
Lightroom Classic can exchange the pick states, but darktable and Bridge versions
that recognize a reject only as `xmp:Rating="-1"` will not show Happy Photon
rejects because the rating remains the true star value. Sidecars may sync through
the folder's cloud provider, while the original photo remains untouched and is
never downloaded for XMP work.
Update discovery is manual-only. Happy Photon makes no automatic update network
requests; it contacts GitHub only when you explicitly choose **Check for updates**
on the About tab.

## 1. Open and survey the shoot

Start in **Library**. Press `G` at any time to return to it.

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

Use the three thumbnail buttons at the right edge of the Library footer to change
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

Burst grouping places photographs captured within two seconds into the same
sequence. It does not choose a winner; it makes neighboring frames easier to
recognize and compare. Happy Photon analyzes capture times only after Bursts is
enabled. Sustained analysis appears in the shared background-activity segment with
processed and total counts; it is absent again after the sweep finishes or Bursts is
turned off.

Online-only photographs stay visible with a cloud badge or placeholder. The folder
status reports how many will not be downloaded automatically. Bursts analyzes local
photographs and reports online-only photographs as skipped. To work with one cloud-only
image, select it and choose **Download and open**; this downloads only that original.

Do not start adjusting every image yet. The first goal is to understand the
shoot and remove obvious misses from consideration.

![Happy Photon Library showing the folder tree, filters, thumbnail grid, and
assessment controls](screenshots/Screenshot_Library.png)

## 2. Cull before you develop

Flags answer **what should happen to this frame?**

- Press `P` to toggle **Picked**.
- Press `X` to toggle **Rejected**.
- Press `U` to return photographs to **Unflagged**.

In Library, these commands affect the selection when it is non-empty, even when the
active photograph is outside it. With an empty selection they affect the active
photograph. In Develop they affect only the active photograph. Pick and Reject assign
the whole target set unless every target already has that flag, in which case they
clear it.

Develop confirms each flag, rating, and color-label change briefly over the
photograph — "Set flag: Picked", "Unset rating: ★★★" — and shows nothing between
changes. Library remains where a photograph's current assessment is on display.

Unflagged is useful for undecided frames. Rejected does not delete a file, and
Picked does not automatically select it for export.

Move quickly on the first pass:

1. Reject clear misses such as accidental frames or unusable expressions.
2. Pick the strongest frame from each moment or burst.
3. Leave uncertain comparisons unflagged and revisit them later.

The assessment buttons beneath the image grid provide the same actions. The
Pick and Reject buttons toggle off when clicked again.

### Add ratings only when they help

Stars answer a different question: **how strong or important is this frame?**
Press `1` through `5` to set a rating and `0` to clear it.

Ratings use the same targets as flags: the non-empty selection in Library, otherwise
the active photograph, and only the active photograph in Develop.

A simple starting method is to flag first, then rate only the picked images:

- `3` — a solid keeper
- `4` — a standout from the shoot
- `5` — one of the very best

The meaning is yours to define. Consistency is more useful than a complicated
rating system.

### Add color labels for another classification

Color labels provide a third, independent assessment axis. Use the assessment swatches
or press `6` through `9` for red, yellow, green, or blue; clicking or pressing the active
color clears it. In Library, a label command targets the non-empty selection even when
the active photograph is outside it, and falls back to the active photograph when the
selection is empty. In Develop it targets only the active photograph. The swatch ring
always describes the active photograph, not the selected target set.

### Filter the result

The Library filter bar uses labeled groups of compact controls that can be
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

Double-click a thumbnail or press `D` to enter **Develop**. Use the left and
right arrow keys to move between visible images without returning to Library.

If an original is online-only, an existing cached preview can still appear, but Happy
Photon does not start a fresh decode until you choose **Download and open**.
If LibRaw cannot decode a locally available file, an actionable message remains pinned
for that photograph (including common unsupported variants such as Nikon HE), and the
Library keeps its failure marker when you return. A successful retry clears both. Source
availability messages take priority over file decode and global runtime messages.

A useful editing order is composition, light, color, and then refinement. You
do not have to touch every control.

Use **Assess** beside **Fit**, or press `Ctrl+B`, to judge the photograph against
an invariant white reference band and mid-gray surround. The session-only mode
works in Develop and fullscreen, re-fits when toggled so the complete reference
field is visible, and never changes edits or exported pixels.

![Happy Photon Develop showing presets, the image viewer, histogram, and
adjustment controls](screenshots/Screenshot_Develop.png)

### Set the composition

Use the controls below the image to:

- rotate in 90-degree steps;
- enter crop mode with `C`;
- straighten the horizon within the crop controls;
- lock the current crop aspect ratio when needed.

Apply the crop with **Apply** or `Enter`. Use **Cancel** or `Escape` to abandon
the current crop operation. Geometry belongs to the individual frame, so crop,
rotation, and horizon settings are never transferred by presets or copy/paste.

### Shape the light

Start with the largest problem and make the smallest adjustment that solves it:

- **Exposure** changes the overall light level in photographic stops.
- **Highlights** adjusts the brighter tonal regions.
- **Shadows** adjusts the darker tonal regions.
- **Brightness** provides another overall brightness adjustment.
- **Contrast** changes the separation between dark and light areas.

Watch the photograph first and use the histogram as supporting information.
Avoid correcting the histogram merely to make it fill the graph.

### Shape the color

- **Kelvin** moves the white balance toward cooler or warmer color.
- **Vibrance** provides a gentler color-intensity adjustment.
- **Saturation** makes the overall color intensity change more strongly.

Kelvin usually answers whether the photograph feels too cool or too warm.
Vibrance and saturation answer whether the color feels too weak or too intense.
If the image already looks right, leave them alone.

### Refine the tone

Use the **Tone Curve** when the basic controls cannot produce the tonal shape
you want. It is a finishing tool, not a required step.

In Develop or fullscreen, press `B` to toggle between the edited image and the
original. In Develop, use `Ctrl+Z` to undo a color or tonal edit and `Ctrl+Y` or
`Ctrl+Shift+Z` to redo it. These editing shortcuts do nothing in Library. The reset
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

Applying a preset replaces the image's current color and tonal settings. Click
the active preset again to remove it and reset those settings. Presets never
include crop, rotation, or horizon changes.

### Copy edits between images

Press `Ctrl+Shift+C` to copy the current image's color, tonal, curve, and preset
settings. Press `Ctrl+Shift+V` to paste them onto another image.

To apply the settings to several photographs:

1. Return to Library with `G`.
2. Select the target images.
3. Press `Ctrl+Shift+V`.
4. Review and confirm the batch operation.

Batch paste is not undoable. Crop, rotation, and horizon settings on every
target remain unchanged.

After sharing a starting point, inspect the images individually. Exposure and
temperature can still vary within a series.

## 5. Build a selection

Selection is a working set shared by Library assessment actions, batch paste, and
export:

- **Picked** means the image passed your cull.
- **Rating** records its relative strength or importance.
- **Selected** means include this image in the next Library action or export.

Filter the Library to the group you want before selecting it. Then:

- press `Space` to toggle the current image;
- use `Ctrl+Click` to add or remove individual images;
- use `Shift+Click` to select a range;
- press `Ctrl+A` to select every image currently visible through the filters;
- press `Ctrl+D`, or choose **Deselect All** from **More library actions**, to clear
  the visible selection.

A plain click or arrow-key move replaces the selection with the newly focused
photo, so single-photo assessment always lands on the photo under the focus ring;
use the modifiers above to build a multi-photo selection.

Images that become hidden by a new filter are removed from the selection. Set the
filters first, then make the final selection.

Press `F` with two or more photos selected to review only that selection in full
screen, starting from the first selected photo in the Library's current order. Navigation stops at the first and last
selected photo, and the `SELECTION` badge shows the current position. The set updates
with visible selection changes; if fewer than two selected photos remain, navigation
returns to the full folder until full screen is entered again. With zero or one photo
selected, full-screen navigation continues through the full folder as usual.

## 6. Export finished copies

From Library, choose **Export**. You can also press `Ctrl+E` from Library or Develop.
The dialog opens even when nothing is selected and reports zero images without
changing the selection.

1. Choose an output folder. The default is an `export` folder beneath the open
   photo folder.
2. Choose JPEG, PNG, or WebP.
3. Set the quality when the selected format uses it.
4. Choose one size:
   - **Hi-Res** applies no output-size limit.
   - **Web** constrains the longest dimension to the specified size.
   - **Small** creates a smaller longest-dimension copy.
5. Choose a naming pattern and check the filename preview.
6. Start the export.

If the selection includes online-only originals, Happy Photon first reports their exact
count and approximate logical size. Choose **Cancel** to leave them untouched or
**Download / Export** to approve downloads for that selected batch. Stopping an export
after approval is best effort because the cloud provider may already have started a
download.

The exported files go directly into the chosen output folder.

Export decodes and edits each photograph, then creates new output files.
Targets that would overwrite a loaded original are refused.

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
8. Return to Library, keep the final filter active, and press `Ctrl+A`.
9. Export the preferred delivery size.

The result is a small, coherent set of finished copies while every original
remains where it started and unchanged.

## Essential shortcuts

| Key | Action |
| --- | --- |
| `G` | Switch to Library |
| `D` | Switch to Develop |
| Arrow keys | Move between images |
| `P` / `X` / `U` | Pick, reject, or unflag |
| `1`–`5` / `0` | Set or clear a rating |
| `6`–`9` | Set red, yellow, green, or blue color label |
| `Space` | Toggle the active photo in the selection |
| `Ctrl+A` / `Ctrl+D` | Select or deselect all visible images |
| `C` | Toggle crop mode |
| `B` | Toggle before/after in Develop or fullscreen |
| `Ctrl+B` | Toggle color assessment mode in Develop or fullscreen |
| `Ctrl+Shift+C` / `Ctrl+Shift+V` | Copy or paste edit settings |
| `Ctrl+Z` / `Ctrl+Y` | Undo or redo color and tonal edits in Develop |
| `Ctrl+E` | Open the export dialog |
| `Ctrl+,` | Open Settings |
| `F` | Toggle image-only fullscreen |

Use the `?` button in the title bar to open **Help & About**. The complete
shortcut reference is selected by default, with build and project information
available on the About tab. About can check for updates manually. Store-packaged
Windows installations open the Microsoft Store, which manages their updates;
other installations open the matching GitHub release. A muted dot on `?` means
an in-session manual check found a newer release, and Help then opens on About.
