# Contributing to Happy Photon

Thank you for helping make a smaller, calmer photo workflow.

## Before opening a change

- Search existing issues before filing a duplicate.
- Use the camera compatibility form for decoder- or camera-specific reports.
- Discuss substantial workflow or architecture changes before investing in a
  large implementation.
- Never attach private catalogs or photographs you do not have
  permission to redistribute.

## Development setup

Happy Photon requires the .NET 10 SDK.

```bash
dotnet restore HappyPhoton.sln --locked-mode
dotnet build HappyPhoton.sln --configuration Release --no-restore
dotnet test HappyPhoton.sln --configuration Release --no-build --no-restore
```

Please keep changes focused and follow [AGENTS.md](AGENTS.md), including these
project rules:

- Never modify original image files.
- Keep every C# and XAML source file under 500 lines.
- Preserve MVVM boundaries.
- Include tests proportional to the risk.
- Before/after screenshots are welcome for visible UI changes, but optional;
  describe the visible change in words when you do not attach them.
- Do not commit native binaries. RAW decoding restores from the committed
  `HappyPhoton.LibRaw.Native` package under `packages/native/`, so a fresh
  clone builds with no extra setup. That package is rebuilt only by
  maintainers through the manually dispatched `build-libraw` workflow and
  committed only after its audit review. See
  [`BUILDING.md`](BUILDING.md).

## Development model

Happy Photon is intentionally AI-developed:

- Avoid hand-writing code. Plan and implement changes through capable AI
  agents.
- Use multiple, different state-of-the-art models for adversarial plan and
  code review; a model should not review its own work.
- Human review is the guidance and approval gate, and the protection against
  over-engineering.

## Commits

- Squash commits before merge so each merged change lands as one commit.
- Commit messages follow the existing standard: a single imperative line
  matching the history (see [AGENTS.md](AGENTS.md)) — no body, no trailers,
  no emoji.

## Pull requests

A pull request should explain the user-visible outcome, tests performed, and
any effect on original-file safety, the catalog, or packaging.
Keep unrelated formatting and refactoring out of the change.

The project may ask for changes before merging. A submitted contribution does
not guarantee acceptance.

## License terms for contributions

Happy Photon is distributed under GPL-3.0-or-later. By submitting a
contribution, you agree to license it under GPL-3.0-or-later.

Contributors retain copyright in their contributions. Happy Photon does not
require a contributor license agreement or copyright assignment, and
submission does not grant the project broader proprietary-relicensing rights.

Only submit work you have the right to contribute. Identify copied or adapted
material and its license in the pull request.
