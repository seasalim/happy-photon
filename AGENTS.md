# Happy Photon Agent Guide

Happy Photon is a cross-platform photo management and editing app for browsing images, editing JPEG/RAW files, and exporting JPEGs. It is inspired by Lightroom, but intentionally smaller and focused on core editing performance.

# Non-Negotiables
- Keep every source file under 500 lines. If a change would push a file past that limit, split focused code into smaller components as part of the same change.
- Update this file when feature or architecture changes make the guidance stale or incomplete.
- Keep code simple, small, and readable. Prefer less code over more code.
- Preserve MVVM boundaries: view state in `ViewModels/`, UI markup in `Views/`, image/catalog logic in `Services/`.
- Original image files must never be modified.
- Minimize comments; add them only when they clarify non-obvious behavior.

# Stack
- C# / .NET 10
- Avalonia UI
- CommunityToolkit.Mvvm
- Magick.NET for image processing and export
- Sdcb.LibRaw for faster RAW processing on Windows/Linux
- SQLite catalog under `~/Pictures/Happy Photon Catalog/`

# Startup
- `Program` acquires `SingleInstanceGuard` before initializing Avalonia so only one Happy Photon process runs at a time.
- `App` shows the main window before initializing the catalog, presets, restored folder tree, and image library; keep non-visual startup work off the first-frame path.

# Project Map
- `docs/ARCHITECTURE.md`: overall architecture; deep detail on catalog loading and the thumbnail pump.
- `Models/`: image metadata, edit settings, app settings, presets, folder nodes.
- `Services/`: image loading, processing, caching, export, catalog, folders, settings.
- `ViewModels/`: MVVM state and commands.
- `Views/`: Avalonia XAML views and code-behind.
- `Assets/`: app icons and bundled resources.
- `HappyPhoton.csproj`: dependencies and build settings.
- `.github/workflows/`: three-platform CI and draft-release automation.
- `docs/release-engineering.md`: signing, notarization, and release setup.

# Refactoring Notes
- Keep service implementations flat in `Services/`; use namespaces and focused class names rather than feature subfolders.
- `MainWindowViewModel` is split into focused partial files by workflow; keep new behavior in the matching partial instead of growing the root file.
- `CatalogService` owns runtime catalog operations; `CatalogSchema` owns SQLite table creation and migrations.

# Build Commands
```bash
dotnet build
dotnet run
dotnet run -c Release
dotnet publish HappyPhoton.csproj -p:PublishProfile=win-x64
dotnet publish HappyPhoton.csproj -p:PublishProfile=linux-x64
./scripts/package-macos.sh
HAPPY_PHOTON_PERF=1 dotnet run
HAPPY_PHOTON_DEBUG=1 dotnet run
```

# C# Style
- Use file-scoped namespaces and nullable reference types.
- Use standard 4-space C# indentation.
- Use PascalCase for types, properties, and methods.
- Use camelCase for locals and parameters.
- Keep XML docs on public APIs when they already exist or clarify an API contract.

# UI Conventions
- Theme tokens live in `Themes/HappyPhotonTheme.axaml` (XAML) and `Views/HappyPhotonColors.cs` (code-behind); design source is `docs/DESIGN.md`. Never hardcode UI hex values.
- Surfaces (low to high): `SurfaceLowest` #0e0e13 (title bar), `SurfaceBase` #131318 (canvas), `SurfaceLow` #1b1b20 (sidebars/status/dialogs), `SurfaceMid` #1f1f25, `SurfaceHigh` #2a292f, `SurfaceHighest` #35343a; borders use `Outline` #849495 and `Divider` #3b494b at 55% opacity.
- Text tiers: `TextPrimary` #e4e1e9, `TextSecondary` #b9cacb, `TextMuted` #849495.
- Accent rule: electric cyan `PrimaryContainer` #00f0ff = act/confirm and active state (buttons, sliders, selection ring, tab underline, active chips); pale cyan `Primary` #dbfcff = luminous foreground; neon magenta `SecondaryContainer` #ff24e4 = highlights such as ratings; lavender `Tertiary` #e1d2ff = passive edit badges. Destructive confirms use `ErrorContainer`.
- Fonts: `FontHeading` (Sora), `FontBody` (Hanken Grotesk), and `FontLabel` (JetBrains Mono), all bundled. Text buttons use `FontLabel` globally; title tabs retain `FontHeading`. Primary panel labels use the uppercase `section-label` class. Font resources: `FontSizeLabel` 12px, `FontSizeBody` 11px, `FontSizeSmall` 10px.
- Radii: `RadiusSmall` 4, `RadiusMedium` 8 (buttons and inputs), `RadiusLarge` 16, `RadiusCard` 24; filter chips are full pills.
- Spacing: 20px between major control groups, 8-10px within groups.
- Use horizontal `StackPanel` for simple inline layouts.
- Use `Grid` with `Auto,*,Auto` columns for left/center/right alignment.
- Use `CompactSlider` for edit controls.

# Main UI Shape
- Window title is "Happy Photon" for OS identity; the extended-client-area app header is the visible title bar, with the smiling photon icon and two-tone wordmark left of the centered Library/Develop tabs and native window controls retained.
- Library mode: folder tree on the left, thumbnail grid in the center.
- Bursts toggle in the library grid header groups frames shot within 2 seconds.
- Develop mode: presets on the left, zoomable image viewer in the center.
- Fullscreen mode: image-only black viewer with panels, status, and edit controls hidden.
- Right panel: exposure, temperature, brightness, contrast, tone curve, reset/undo/redo.
- Status bar: folder path, image count, selection count, first selected filename, agent toggle, and the "HAPPY PHOTON" build-info label.

# Keyboard Shortcuts
- `G`: Library mode.
- `D`: Develop mode.
- `F`: image-only fullscreen mode.
- `E`, `Escape`, `Enter`: toggle Library/Develop.
- Arrow keys: previous/next image; up/down by row in Library mode.
- `Space`: toggle export selection.
- `Delete`: delete current image.
- `B`: before/after view.
- Mouse wheel: zoom in Develop mode.
- Drag when zoomed or middle-drag: pan.
- Double-click thumbnail: enter Develop mode.
- `Ctrl+E`: batch export panel.
- `Ctrl+Shift+C`: copy edit settings from the current image.
- `Ctrl+Shift+V`: paste edit settings; in Library with a selection, applies to all selected images after confirmation (not undoable).
- `Ctrl+Z`: undo edit; `Ctrl+Y` or `Ctrl+Shift+Z`: redo.
- `1`-`5`: set star rating on current image; `0` clears; pressing the current rating again is a no-op.
- `Ctrl+A`: select all images for export.
- `Ctrl+Click`: toggle export selection.
- `Shift+Click`: range select images for export.
- Folder tree: arrow keys retain standard tree navigation; clicking a folder or pressing `Enter` moves keyboard focus to the library grid. The active folder remains highlighted after focus moves.

# Catalog
- Persistent state lives in `~/Pictures/Happy Photon Catalog/catalog.db`.
- Cached thumbnails and previews live under `~/Pictures/Happy Photon Catalog/assets/`.
- Thumbnail cache encoding uses a bounded 256-entry, drop-oldest channel with one background writer so uncached images render before persistence without unbounded memory growth.
- Thumbnail cache files are explicit JPEGs encoded from direct BGRA pixel import; legacy PNG bytes under `.jpg` names migrate lazily when read.
- Thumbnail cache writes use `assets/tmp/` for atomic moves; catalog initialization clears orphaned temp files. Shutdown drains the writer for at most two seconds, then abandons pending cache entries so a slow catalog volume cannot block window close.
- `images` stores file metadata, edit settings, applied preset, flag state, and star rating. Older catalogs may retain unused `has_thumbnail` / `has_preview` columns; runtime cache validity comes from asset-file timestamps.
- `app_settings` stores app preferences.
- Startup initializes the catalog and restores the previous folder tree state.
- Folder loads register missing images and restore state with `LoadOrCreateImageStatesAsync`; do not reintroduce per-image catalog query loops.
- Batch edit persistence uses one gated SQLite transaction and updates in-memory image settings only after the whole transaction commits.

# Image Processing
- `ImageService` is a facade over thumbnail, preview, export, histogram, and edit-application services.
- `MetadataService` extracts metadata into a plain DTO off-thread, deduplicates concurrent loads per `ImageFile`, and awaits UI-thread application before callers continue.
- `BitmapConversionService`, `ImageServiceHelpers`, and `EmbeddedJpegExtractor` are shared helpers.
- Uncached JPEG thumbnails use aspect-compatible embedded EXIF thumbnails when available, then `JpegThumbnailDecoder` for reduced-size platform decoding and EXIF orientation; retain the Magick.NET fallback for unsupported, corrupt, or mismatched embedded thumbnails.
- Standard formats: JPG, PNG, BMP, GIF, TIFF, WebP.
- HEIC/HEIF requires platform codecs: libheif on Linux, HEIF Image Extensions on Windows.
- RAW formats include CR2/CR3, NEF/NRW, ARW/SRF/SR2, DNG, RAF, ORF, RW2, and PEF.

# RAW Notes
- Use `IRawProcessingService` for platform-specific RAW handling.
- Windows/Linux use `LibRawProcessingService`; macOS uses `MagickNetRawService`.
- LibRaw is for RAW decoding only, not export.
- For LibRaw RGB output, add a PPM header with `CreatePpmImage()` before loading into MagickImage.
- RAF files must avoid Magick.NET fallback on Windows.
- For embedded JPEG extraction, find the last FFD9 marker, not the first.

# Export
- Export through Magick.NET to JPEG, PNG, or WebP.
- UI size presets are Hi-Res (original size), Web, and Small; a single variant stays in the output folder, while multiple variants use per-variant sub-folders.
- Decode and apply edits once per image, then write variants in descending size order using progressive downscaling.
- The agent `export_images` tool accepts `format` and free-form `variants`; explicit agent variants always use sanitized per-variant sub-folders.
- Exports can never overwrite original image files; targets colliding with any loaded original are refused in both the UI and agent tool (`Services/ExportSafety.cs`).
- Apply edits before export: exposure, white balance, brightness, contrast, tone curve, rotation, horizon rotation, and crop.
- Copy/paste transfers color and tonal settings, tone curve, and preset id only; geometry never transfers.

# Presets
- Happy Photon currently supports user-defined presets only; do not add built-in presets until the editing primitives have visual acceptance tests.
- User presets are JSON files under `<CatalogPath>/presets/`, managed by `PresetService`.
- User presets appear in the `My Presets` section and capture color/tonal settings only, never geometry.
- User preset ids and filenames remain stable across rename and overwrite operations.
- Applying a preset replaces current edits and pushes the previous state to undo.
- Clicking the active preset again resets/untoggles it.
- `EditSettings.AppliedPresetId` persists active preset state per image.

# Performance Notes
- Folder thumbnail loading uses six long-lived priority workers after the initial 12 images. The grid reports its visible range, nearby images are prefetched, and decoded residency is capped at 512; do not restore eager whole-folder loading.
- Active-library thumbnail ownership uses a reference-identity set. Failed thumbnail decodes are remembered on the current `ImageFile` and skipped by later viewport requests without clearing a prior good bitmap; reopening the folder creates fresh instances and retries.
- Do not create one semaphore waiter or cancellation registration per image. The active thumbnail session owns its request CTS, folder switches cancel only, and generation checks dispose late results instead of mutating stale library state.
- Folder enumeration and batched catalog-state loading run off the UI thread; Microsoft.Data.Sqlite async calls may still perform synchronous disk work.
- Keep slider edits responsive by using cached previews and cloning instead of re-decoding.
- Preview-cache JPEG encoding and disk writes use a bounded background writer and must never run while the in-memory preview gate is held.
- Histogram calculation is intentionally deferred/debounced during slider interaction.
- Library-mode histograms use the selected edited thumbnail and must not trigger preview decoding or preview-cache writes.
- Bitmap conversion should use direct pixel copy through `WriteableBitmap.Lock()`.
- `ZoomPanControl` supports auto-fit on resize through `AutoFit` and `AutoFitRequested`.

# Agent Access (MCP)
- Agent access is off by default and runs inside the GUI process when the status-bar `Agent` toggle is enabled.
- The server listens only on `127.0.0.1:7326` at a persisted 32-character token path; missing or incorrect token paths return 404.
- The privacy contract is metadata and local thumbnail-derived statistics only. MCP tools never return image pixels or image content.
- `get_image_stats` always measures the unedited base thumbnail, regardless of cache state.
- Keep the three layers separate: `McpServerHost` owns MCP/ASP.NET types, `AgentToolService` owns plain-C# DTO validation and UI-thread marshaling, and `MainWindowViewModel.Agent` owns app mutations.
- The nine tools are `get_library_state`, `list_images`, `get_image_stats`, `set_rating`, `set_flag`, `list_presets`, `apply_preset`, `apply_edit_settings`, and `export_images`.
- Folder loads eagerly sweep metadata in the background. `list_images` exposes nullable `burstId`, `burstIndex`, and `burstSize`; `get_library_state.burstsComputed` distinguishes pending analysis from an ungrouped image.
- Agent exports are JPEG, PNG, or WebP copies beneath the currently open folder, skip existing files, and reject original-image collisions, path traversal, or within-batch name collisions.
- `McpServerEnabled` and `McpToken` are persisted app-setting keys. Preserve both in immediate saves and shutdown saves.
- The self-contained single-file Windows publish bundles the ASP.NET Core framework used by the embedded server; no additional target-machine runtime is required.
- Public macOS support is Apple Silicon only (`osx-arm64`); do not publish an Intel/x64 artifact. `scripts/package-macos.sh` creates an ad-hoc-signed `.app` under `artifacts/` for local testing; public distribution requires Developer ID signing, hardened runtime, and Apple notarization.

# Testing
- xUnit tests live in `Tests/` (`HappyPhoton.Tests.csproj`); name files `*Tests.cs`.
- Run with `dotnet test HappyPhoton.sln`.
- `HappyPhoton.csproj` excludes `Tests\**` from its compile glob and grants `InternalsVisibleTo` to `HappyPhoton.Tests`; keep both when restructuring.
