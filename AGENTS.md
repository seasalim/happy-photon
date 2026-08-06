# Happy Photon

Happy Photon is a performance-focused .NET 10/Avalonia photo workflow for browsing and non-destructively editing JPEG and RAW files.

## Repository rules

- Never modify original images; exports must also refuse collisions with loaded originals.
- Keep every source file under 500 lines; split focused components when needed.
- Preserve MVVM ownership: state in `ViewModels/`, UI in `Views/`, and image/catalog logic in `Services/`.
- For new features, make the smallest simple change that works; optimize only after measurement identifies a real performance need.
- Preserve the startup, catalog, thumbnail, preview, and render performance invariants documented in `docs/ARCHITECTURE.md` and `docs/pipeline/`.
- Use theme resources from `Themes/HappyPhotonTheme.axaml` and `Views/HappyPhotonColors.cs`; never hardcode UI colors.
- Keep service implementations flat in `Services/`. Extend the matching `MainWindowViewModel` partial instead of growing its root file.

## Load context on demand

Read only the material relevant to the change:

- Architecture, startup, catalog, threading, or thumbnails: `docs/ARCHITECTURE.md`
- Decode, edits, preview, histogram, RAW, or export: start with `docs/pipeline/OVERVIEW.md`, then follow its relevant sibling document
- UI or workflow behavior: `docs/DESIGN.md` and, when user flow matters, `docs/WORKFLOW.md`
- Packaging or releases: `docs/release-engineering.md`

Treat code and tests as the specification for details not covered there. Update the relevant focused documentation when behavior or architecture changes; do not grow this file with feature inventories.

## Verify

Run `dotnet test HappyPhoton.sln`.
