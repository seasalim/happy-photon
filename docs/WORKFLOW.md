# The Happy Photon Workflow

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
Photon catalog:

```text
~/Pictures/Happy Photon Catalog/
```

Edits are non-destructive: they are instructions stored in the catalog, not
changes written into the original image. Export creates new files.

Happy Photon is pre-1.0 software. Keep a backup of important photographs and
consider learning the workflow with a copied shoot.

## 1. Open and survey the shoot

Start in **Library**. Press `G` at any time to return to it.

1. Choose a folder in the folder tree on the left.
2. Click the folder or press `Enter` to move focus to the image grid.
3. Let the first thumbnails appear, then move through the shoot with the arrow
   keys or by clicking thumbnails.
4. Turn on **Bursts** when the folder contains sequences of closely spaced
   frames.

If photographs are added, removed, or renamed outside Happy Photon, use the
**Refresh folder** button beside **Change…** in the Folders header. Refresh
re-reads the currently viewed folder while preserving active filters and
cataloged edits, ratings, and flags for paths that still exist.

Burst grouping places photographs captured within two seconds into the same
sequence. It does not choose a winner; it makes neighboring frames easier to
recognize and compare.

Do not start adjusting every image yet. The first goal is to understand the
shoot and remove obvious misses from consideration.

![Happy Photon Library showing the folder tree, filters, thumbnail grid, and
assessment controls](screenshots/Screenshot_Library.png)

## 2. Cull before you develop

Flags answer **what should happen to this frame?**

- Press `P` to toggle **Picked** on the current image.
- Press `X` to toggle **Rejected**.
- Press `U` to return it to **Unflagged**.

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

A simple starting method is to flag first, then rate only the picked images:

- `3` — a solid keeper
- `4` — a standout from the shoot
- `5` — one of the very best

The meaning is yours to define. Consistency is more useful than a complicated
rating system.

### Filter the result

The Library controls can be combined to show:

- all files, RAW files, or JPEG files;
- all flags, picked images, or rejected images;
- all ratings or a minimum star rating.

For example, choose **Picked** and **3+** to review the photographs most likely
to be delivered. Changing a flag or rating while a filter is active can make
the current image disappear when it no longer matches; Happy Photon advances
to another visible image.

**Delete Rejected** is a separate, destructive cleanup action. After
confirmation, it moves every rejected image in the open folder to the operating
system Trash. Rejecting alone never moves or deletes the original.

## 3. Develop the keepers

Double-click a thumbnail or press `D` to enter **Develop**. Use the left and
right arrow keys to move between visible images without returning to Library.

A useful editing order is composition, light, color, and then refinement. You
do not have to touch every control.

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

Press `B` to toggle between the edited image and the original. Use `Ctrl+Z` to
undo a color or tonal edit and `Ctrl+Y` or `Ctrl+Shift+Z` to redo it. The reset
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

## 5. Select what to export

Export selection is deliberately separate from flags and ratings:

- **Picked** means the image passed your cull.
- **Rating** records its relative strength or importance.
- **Selected** means include this image in the next export.

Filter the Library to the group you want before selecting it. Then:

- press `Space` to toggle the current image;
- use `Ctrl+Click` to add or remove individual images;
- use `Shift+Click` to select a range;
- press `Ctrl+A` to select every image currently visible through the filters;
- use **Select None** to clear the visible selection.

Images that become hidden by a new filter are removed from the export
selection. Set the filters first, then make the final selection.

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

The exported files go directly into the chosen output folder.

Export decodes and edits each photograph, then creates new output files.
Targets that would overwrite a loaded original are refused.

## A complete first workflow

For a first shoot, keep the process deliberately simple:

1. Open the folder and enable Bursts if it contains rapid sequences.
2. Make one quick pass with `P`, `X`, and `U`.
3. Filter to Picked and give only the strongest images three to five stars.
4. Filter to Picked and `3+`.
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
| `Space` | Toggle export selection |
| `C` | Toggle crop mode |
| `B` | Toggle before/after |
| `Ctrl+Shift+C` / `Ctrl+Shift+V` | Copy or paste edit settings |
| `Ctrl+Z` / `Ctrl+Y` | Undo or redo color and tonal edits |
| `Ctrl+E` | Open the export dialog |
| `F` | Toggle image-only fullscreen |

Use the `?` button in the title bar to open **Help & About**. The complete
shortcut reference is selected by default, with build and project information
available on the About tab.
