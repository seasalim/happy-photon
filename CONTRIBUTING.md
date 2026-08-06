# Contributing to Happy Photon

Thank you for helping make a smaller, calmer photo workflow.

## Before opening a change

- Search existing issues before filing a duplicate.
- Use the camera compatibility form for decoder- or camera-specific reports.
- Discuss substantial workflow or architecture changes before investing in a
  large implementation.
- Never attach private catalogs, agent tokens, or photographs you do not have
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
- Include before/after screenshots for visible UI changes.

## Pull requests

A pull request should explain the user-visible outcome, tests performed, and
any effect on original-file safety, the catalog, agent access, or packaging.
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
