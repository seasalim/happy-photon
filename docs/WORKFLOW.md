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

Happy Photon works directly with existing folders. Originals stay in place; edits,
assessments and presets live in the local catalog, and Export creates new files.
Keep the catalog in your backups. Thumbnails and previews are regenerable caches;
Settings → Storage reveals both locations and stages moves for the next launch.

First run confirms storage and the Pictures browsing root, optionally imports Lightroom
assessments, then offers a tour. The title-bar Theme menu chooses Dark or Middle Gray;
Settings (gear or `Ctrl+,`) holds preferences and Metadata interop controls.
See [DESIGN.md](DESIGN.md) for first-run and theme details.

Cloud-only originals are not automatically downloaded. Cached thumbnails/previews may
remain visible; source access requires Download and open for one photo or confirmation
of an export batch. Happy Photon is pre-1.0; learn with backed-up photographs.

## Bring assessments from Lightroom Classic

Close Lightroom Classic, then choose **Import from Lightroom…** from the Folders
header or optional first-run step. Choose a detected catalog or browse for a .lrcat.
Review matched locations, map moved roots or leave them blank to skip, and choose
Lightroom-wins or fill-empty policy. The preview updates before Apply; import never
clears assessments or writes to Lightroom/originals.

Crop import is separately opt-in: it approves a header-only orientation check on local
candidates. Supported zero-angle crops fill only empty geometry; angled/warped crops
and existing geometry remain unchanged. No original is decoded or downloaded.
Missing mapped files are skipped; if none exist, fix mappings before Apply becomes
available. Reports distinguish nothing to import from nothing matched, and list virtual
copies/unsupported files as skips. Repeating an up-to-date import performs no writes.

### Exchange assessments with XMP

Settings → Metadata enables per-catalog XMP reading or read/write interop. Ratings,
flags, recognized labels and supported plain crops exchange with V1 only; V2–V8 stay
catalog-only. Imported crops fill empty geometry and never replace an existing edit.
Read/write publishes changed assessments and portable crops; original images stay
untouched. Crop interop reads only locally available orientation headers.

To publish older state, choose **Write XMP sidecars** from the thumbnail context menu
in Read & write mode. It uses selection or the active photo; `Ctrl+A` selects the folder.
Re-run it for pending writes after a folder switch. Sidecars can sync through the cloud
provider. Lightroom-compatible pick state preserves true star ratings, so applications
that recognize rejects only as negative ratings may not display Happy Photon rejects.

## 1. Open and survey the shoot

Start in **Browse**. Press `G` at any time to return to it.

The review pane shows the active photo's histogram, file, camera and location details;
multiple selection adds count, capture-date range and local file size; online-only
originals stay out of date/size aggregates until downloaded. Hover its name for the
folder, or right-click → Copy details. Coordinates open OpenStreetMap only when clicked.
A muted file-modified date is a display fallback, not capture time.

1. Choose a folder in the folder tree on the left.
2. Click the folder or press `Enter` to move focus to the image grid.
3. Let the first thumbnails appear, then move through the shoot with the arrow
   keys or by clicking thumbnails.
4. Turn on burst grouping with the stacked-frames icon when the folder contains
   sequences of closely spaced frames.

Small, Medium and Large in the Browse footer control density; the choice persists.
Large retains existing thumbnails while sharper local versions load. A ! marks a
thumbnail or RAW Develop failure; native installation failure is a global status.
Refresh folder in the Folders header re-reads images/subfolders while preserving
filters and catalog state for paths that remain.

Right-click thumbnails for Copy path, Reveal, and Versions. New Version from Current
copies the interpretation into a sibling, up to eight. Rename version label sets an
optional badge; Delete version removes only V2–V8 catalog/cache state after confirmation.
Right-click outside the selection selects that photo; inside preserves the selection.
Copy path copies selected paths in grid order. Reveal selects the active file on
Windows/macOS or opens its folder on Linux; the folder-tree menu offers Reveal only.

Delete and the `Delete` key use the same targets as other Browse actions: the grid
selection when it is non-empty, otherwise the active photograph. After one confirmation,
Happy Photon deletes selected V2–V8 interpretations from the catalog without affecting
their original files, and moves selected primary originals and their resolved XMP
sidecars to the system Trash. A failed file does not stop the rest
of a batch; the final dialog names every failure or skipped sidecar. Online-only files
and sidecars are never downloaded for deletion. Network locations and removable media
are refused because their deletes may not be recoverable. On Windows, a fixed drive
with Recycle Bin disabled may delete permanently.
Deleting a file removes all of its versions from the catalog and grid.

Bursts groups frames captured within two seconds without choosing a winner. Enable it
to analyze local capture times; the status segment shows processed/total, and a transient
completion message reports skipped cloud files. Disabling it stops remaining analysis.

The **J+R** footer toggle starts off, showing RAW and JPEG files separately. Turn it on
to combine same-folder, same-name files into one JPEG tile; the choice is remembered.
Rating, flagging, or labeling that tile assesses both primary files. In Develop, press
`Shift+R` or use **J|R** beside Before/After to switch instantly between the camera JPEG and
RAW while keeping the zoomed viewport. The switch changes files, so it clears undo
history; moving to another capture returns to its JPEG. Turn pairing off to browse,
assess, or export the physical files separately.

Online-only photographs carry a cloud badge or placeholder. Select one and choose
**Download and open** to approve access to that original.

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

Develop and Browse Loupe briefly confirm assessment changes over the photo; Browse
shows persistent state. Rejected does not delete, and Picked does not select for export.
On the first pass, reject clear misses, pick each moment's strongest frame and leave
uncertain comparisons unflagged. Footer buttons perform the same actions.

### Add ratings only when they help

Stars answer a different question: **how strong or important is this frame?**
Press `1` through `5` to set a rating; repeating a star value clears it when every
target already has that rating. Press `0` to clear it directly.

Ratings target the same photographs as flags. One useful convention is three stars
for a keeper, four for a standout and five for your best; consistency matters most.

### Add color labels for another classification

Color labels provide a third, independent assessment axis. Use the assessment swatches
or press `6` through `9` for red, yellow, green, or blue; clicking or pressing the
active color clears it. Label commands use the same targets as flags and ratings. The
swatch ring always describes the active photograph, not the selected target set.

### Filter the result

Combine file-type, flag, minimum-rating and color-label filters. For example, Picked
plus the third star shows picked images rated at least three. Re-click an active
filter to clear that group; the empty result's Clear resets every group. Assessment
changes may remove a photo from the view and advance to the next visible image.

**Delete Rejected** is a separate, destructive cleanup action. After
confirmation, it moves every rejected image in the open folder to the operating
system Trash. Rejecting alone never moves or deletes the original.

## 3. Develop the keepers

Double-click a thumbnail or press `D` to enter **Develop**. Use the previous and next
buttons below the image, or the left and right arrow keys, to move between visible
images without returning to Browse.

A matching cached preview can show the photo and display scopes while the original
loads. Sustained preparation appears in the shared status bar; edits remain available.
An unsupported file keeps an actionable message and Browse failure marker until a
successful retry. Start with composition, then light, color and refinement.

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

Rotate in 90-degree steps with the controls below the image. Enter crop mode
with the **Crop** toggle beneath the histogram or with `R`; its settings then sit
at the top of the adjustment stack:

- straighten the horizon;
- lock the current crop aspect ratio when needed;
- reset the crop.

Apply the crop with **Apply** in the crop header or `Enter`. Use **Cancel** or
`Escape` to abandon the current crop operation. Geometry belongs to the individual frame, so crop,
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
  every touched band; double-click resets one slider, while the Develop footer Reset
  clears all bands with the other color and tonal adjustments.

For a true monochrome RAW, the camera profile, white balance, Saturation, Vibrance,
color mixer, and R/G/B channel-curve controls stay visible but disabled. Existing saved color
values are preserved for later color sources; Exposure, the composite curve, tone,
detail, effects, scopes, and export continue to work normally.

### Refine the tone

Use Tone Curve for finer shaping. RGB edits the composite; R/G/B edit channels and
show touched letters. Its Reset affects only the active curve; footer Reset clears
all curves with the other tonal adjustments.

### Refine detail

Use **Luma NR** for luma grain, **Sharpen** for capture detail, and **Chroma NR** for
color speckling. All three work on RAW, JPEG, HEIC, and TIFF. Noise reduction runs after
tone, so revisit it after a large shadow or exposure change. The Develop viewer uses
bounded previews even at 1:1; judge subtle detail on an export-scale render.

### Add finishing effects

Use **Vignette** to darken corners with negative values or lift them with positive
values; Midpoint moves the onset and dims when inactive. Grain adds monochrome texture
in Fine/Med/Coarse output-pixel sizes. Both work on every source. During crop, vignette
centers on the full temporary canvas and recenters when the crop commits.

Press `\` for Before/After in Develop/fullscreen, or Y/Y|Y in Develop for a
synchronized split. Re-click or Escape closes the split. History below Presets lists
commits newest-first: click a step, Ctrl+Z back, Ctrl+Y/Ctrl+Shift+Z forward. A new edit
truncates redo; right-click → Clear History Above This Step or Alt-click truncates now.
These history actions are unavailable in Browse and during crop. Crop plus provisional
Horizon commits once. Reset preserves crop/rotation/horizon; use their own reset controls.

Edits are saved to the catalog automatically. Export is not required to
preserve the edit instructions.

## 4. Keep a series coherent

### Use a personal preset

Choose Save Current, name the preset, then hover to preview it and click to apply.
Presets replace color, mixer, tonal, curves, detail and effects. Re-clicking the active
preset resets that look; image-specific geometry, profiles and locals stay unchanged.

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

Press `F` or the Develop fullscreen button to review the current selection when it
contains two or more photos. The SELECTION badge shows position; navigation stops at
its ends. Dropping below two restores folder navigation until fullscreen is re-entered.

From the Browse grid, `E`, `Enter`, `Space` or the E footer button opens Loupe,
keeping folders, review pane and assessments visible. Arrows traverse a 2+ selection
without changing it, otherwise visible photos. Space/Z toggles Fit/1:1; E/G/Escape
returns to the grid, D enters Develop and F opens fullscreen.

Select two to four photos and choose X|Y or press C for Compare: two side by side,
three/four in a grid. Click a pane or use arrows for the active photo; assessments
apply there. Fit, zoom, pan and loupe peek synchronize. Re-click X|Y or Escape returns
with selection/focus preserved. C also enters Compare from Loupe.

## 6. Export finished copies

Choose the **Export** workspace to prepare finished copies of the Browse selection.
The left batch list shows thumbnails, filenames and version labels; all listed photos
export. Clicking a row changes only the preview. **Change photos…** offers exactly
**Choose in Browse…** (preserves selection) and **Use picked photos (N)** (replaces the
selection with picked photos in the current Browse view, respecting filters). With no
visible picks, it is disabled with “No picked photos in the current view”. Re-entering
Export rebuilds the batch. The empty state also offers **Choose in Browse…**.
Changing photos or settings during a run prepares the next batch; the running job is fixed.

1. Choose a **Destination** folder; the default is `export` beneath the open folder.
2. Choose any combination of **Output sizes**: **Full size · No resizing**, **Web**, and
   **Small**. Web and Small specify long-edge pixels and preserve aspect ratio. Enabled
   sizes must be whole numbers from 16 to 65,536; invalid text stays visible and blocks
   Export with an inline reason. Values are never silently clamped.
3. Choose JPEG, PNG, WebP, or 16-bit TIFF. JPEG/WebP show Quality; PNG/TIFF show **Lossless**.
4. Choose **Remove location data** if the copies should omit GPS metadata.
5. Expand **More options** for color space, output sharpening and filenames. Its summary
   shows the current choices. **Keep original filename** is the default; **Custom pattern**
   accepts `{name}` and `{date}`, where `{date}` is the export date.
6. Expand **Watermark** and choose **Add watermark** to stamp single-line text on every
   exported size and on **Preview output**; Develop and thumbnails stay unmarked. Font,
   style, size, color, opacity, edge, alignment, side-edge rotation and margin persist
   across sessions; size and margin follow the short edge. Blank enabled text blocks Export.
7. Review **Example for this photo**: the relative output path includes the format extension,
   size subfolder and any version suffix. One size goes directly into the destination;
   multiple sizes each get a subfolder. Multiple versions of the same file in a batch get
   stable `-V<n>` suffixes; a single version keeps its ordinary name.
8. Choose **Export N files** or press `Enter`. The fixed footer keeps the photo/size count,
   validation reason and action visible, including at the minimum window size.

The center shows the standard preview immediately. **Preview output** is opt-in: it
previews size, color space and output sharpening, not JPEG/WebP compression quality. Its
chooser offers valid enabled sizes, initially the largest (Full means no resizing), and
falls back to the largest valid enabled size when the choice becomes disabled or
invalid. The caption names the accepted size and cap, not measured bitmap dimensions,
and color space. While a refresh is pending, **UPDATING…** is appended to the facts for
the pixels still displayed. Turning it off restores **PREVIEW · edits applied** and the
standard preview. This adds no automatic full-resolution decode or cloud download. Only
an accepted proof uses its output pixel dimensions as the native-size cap; Proof off
restores original-relative fit.

If the selection includes online-only originals, Happy Photon first reports their exact
count and approximate logical size. Choose **Cancel** to leave them untouched or
**Download / Export** to approve downloads for that selected batch. Stopping prevents further
output installation; cancellation of an already-started cloud download is best effort because
the provider may finish it.

Before work starts, Happy Photon refuses targets matching loaded originals or another
target in the same job. Existing output files are confirmed together. Copies go to the
chosen destination (in size subfolders for multiple sizes); a file that appears after
the confirmation pass is not overwritten.

The **Exporting** strip shows **Exporting k of N files** and **Stop export**. Stop keeps
already-written files and reports **Export stopped** with **k of N files completed and kept.**
The disabled footer action reads **Export in progress…** and explains that changes prepare
the next batch. Ctrl+Shift+E enters Export; Escape returns to the previous workspace
without stopping the job. The job continues across workspaces.
The footer report keeps completion counts visible. **Open folder** appears when files were
written and opens that job's destination, even if the current settings have changed.
**Show details** reveals failures and warnings in a bounded scrolling area; **Retry failed only**
retries the saved targets
without rerunning successful siblings. Collision remedies point back to Browse.

## A complete first workflow

Open the shoot, optionally enable Bursts, then make a fast flagging pass. Filter to
keepers, rate the strongest, develop a representative image and share its starting
look. Inspect each result before selecting the finished set and exporting copies.

## Essential shortcuts

Help & About lists every shortcut and gesture; these are the essentials.

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
| `W` | Toggle the white-balance eyedropper in Develop |
| `Delete` | Delete selected versions or move primary originals to Trash after confirmation |
| `Shift+W` | Toggle Locals in Develop |
| `B` | Open Locals if needed and arm Brush in Develop |
| `[` / `]` | Scale brush radius by ×0.8 / ×1.25 while the Brush section is visible |
| `Shift+[` / `Shift+]` | Change brush Feather by −10 / +10 points |
| Hold `Alt` | Invert Paint/Erase for a stroke started while held; the cursor shows the effective mode and the toggle stays unchanged |
| `O` / hold `M` | Toggle Show Mask / temporarily show the mask while Locals is open |
| `\` | Toggle before/after in Develop or fullscreen |
| `Y` | Show Before and After side by side in Develop |
| `Shift+R` | Switch between a paired JPEG and RAW in Develop |
| `L` | Toggle color assessment mode in Develop or fullscreen |
| `Space` / `Z` | Toggle Fit and 1:1 in Develop or Browse Loupe |
| `J` | Toggle clipping overlay in Develop |
| `Ctrl+Shift+C` / `Ctrl+Shift+V` | Copy or paste edit settings |
| `Ctrl+Z` / `Ctrl+Y` / `Ctrl+Shift+Z` | Move backward or forward through Develop history |
| `Ctrl+Shift+E` | Open the Export workspace |
| `Enter` | Run Export, apply crop, or move from Browse Loupe to Develop |
| `Ctrl+,` | Open Settings |
| `F` | Toggle image-only fullscreen |
| `Escape` | Exit Browse Loupe or Compare, cancel crop, or return from a transient view |

### Gesture map

Press-and-hold loupe, synchronized Compare and Before/After gestures are listed with
keyboard shortcuts in Help & About. Their scopes keep review actions distinct from edits.

Use the `?` button in the title bar to open **Help & About**. The complete
shortcut and gesture reference is selected by default, with build and project information
available on the About tab. Update discovery is manual-only: Happy Photon makes
no automatic update network requests, and About contacts GitHub only when you
choose **Check for updates**. Store-packaged Windows installations open the
Microsoft Store, which manages their updates; other installations open the
matching GitHub release. A muted dot on `?` means an in-session manual check
found a newer release, and Help then opens on About.
