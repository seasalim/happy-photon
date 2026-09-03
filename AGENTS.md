# Happy Photon

Happy Photon is a performance-focused .NET 10/Avalonia photo workflow for browsing and non-destructively editing JPEG and RAW files.

## Repository rules

- Never modify original images; exports must also refuse collisions with loaded originals.
- Background work must never hydrate cloud-only originals; source content reads require
  a live availability check and explicit, clearly scoped user approval.
- Keep every source file under 500 lines; split focused components when needed.
- Preserve MVVM ownership: state in `ViewModels/`, UI in `Views/`, and image/catalog logic in `Services/`.
- For new features, make the smallest simple change that works; optimize only after measurement identifies a real performance need.
- Preserve the startup, catalog, thumbnail, preview, and render performance invariants documented in `docs/ARCHITECTURE.md` and `docs/pipeline/`.
- Use theme resources from `Themes/HappyPhotonTheme.axaml` and `Views/HappyPhotonColors.cs`; never hardcode UI colors.
- Keep service implementations flat in `Services/`. Extend the matching `MainWindowViewModel` partial instead of growing its root file.

## Planning

- A plan states the goal, the approach in a few sentences, the main touch points, and any genuinely non-obvious decision or risk. That is the whole plan.
- Budgets: inline plans under 10 lines; a written plan document for even a large feature fits on roughly one page.
- Decisions belong in plans; discovery and implementation detail do not. Never include: exhaustive code audits or inventories (file:line tables, reference counts), restated current behavior beyond a short paragraph, derivations or proofs, exact SQL, method signatures, or code snippets. The implementer finds and decides those.
- Match detail to risk. Expand a step only when it is genuinely ambiguous or risky; routine steps get one line or none.
- Every work-package spec carries a one-line net-LOC expectation and a "what does this delete" clause naming the code the change supersedes or retires; report the actual net production diff at the merge gate. "Nothing" is an acceptable answer only with a stated reason.
- Do not enumerate file-by-file edits, edge-case matrices, phases, or contingency branches unless asked.
- A plan that will not fit its budget usually means the change should be split — propose the smaller first slice instead.

## Measured change process

- For refactors and fixes, first establish an instrumented baseline and propose concrete gates that include the workload, metric, observed before value, and acceptance threshold. Get the user's explicit approval of those gates before changing production code.
- Instrumentation and test-only changes may be made while establishing the gates. If a meaningful instrumented gate is impractical, explain why and agree on an observable substitute before starting the production fix.
- Split production work into the smallest coherent, human-reviewable iteration. Keep each production diff narrowly focused; test-code changes do not count toward this limit.
- After each production iteration, rerun the approved gates, report the before/after values, and walk the user through every production change and how the design preserves ownership and relevant invariants before continuing.

## Commits

- Commit messages are a single imperative line matching the existing history (e.g. "Highlight the target of each workflow tour step").
- No body, no bullet lists, no "Co-Authored-By" or "Generated with" trailers, no emoji.
- Squash commits before merge so each merged change lands as one commit.

## Load context on demand

Read only the material relevant to the change:

- Architecture, startup, catalog, threading, or thumbnails: `docs/ARCHITECTURE.md`
- Decode, edits, preview, histogram, RAW, or export: start with `docs/pipeline/OVERVIEW.md`, then follow its relevant sibling document
- UI or workflow behavior: `docs/DESIGN.md` and, when user flow matters, `docs/WORKFLOW.md`
- Packaging or releases: `docs/release-engineering.md`
- Adding or changing a test that waits on time: `docs/test-waits.md`

Treat code and tests as the specification for details not covered there. Update the relevant focused documentation when behavior or architecture changes; do not grow this file with feature inventories.

## Verify

Run `./scripts/verify.ps1`.
