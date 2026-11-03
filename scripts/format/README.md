# Formatter commands

Pinned formatters for explicitly named files: C# (`dotnet format whitespace`), PowerShell
(PSScriptAnalyzer `Invoke-Formatter`) and Prettier (JS, MJS, TS, CSS, JSON). PowerShell 7.4 or later:

```powershell
pwsh scripts/format/install-format-tools.ps1          # the only download step; once per clone
pwsh scripts/format/check-adopted.ps1                 # the enforced check (verify.ps1 policy and CI run it)
pwsh scripts/format/check-format.ps1 -Adopted         # read-only, every adopted file
pwsh scripts/format/check-format.ps1 <files...>       # read-only, named files
pwsh scripts/format/apply-format.ps1 <files...>       # rewrites files whose only finding is DRIFT
pwsh scripts/format/qualify-format.ps1 [-EvidencePath <file>] [-DriftReport <file>] [-CompareWith <file>]
```

Check and apply exit 0 when every selected file is clean, 1 when findings remain and 2 when the run was
refused or a tool failed; exit 2 reports no verdicts and writes nothing. A parser diagnostic is a `PARSE`
finding; any other formatter failure is exit 2. `check-adopted.ps1` uses the same codes. Qualification exits
0 when every acceptance property held, 1 otherwise and 3 when an entrypoint is missing.

## Adopted scope

`config/scope.json` `adopted` lists repository-relative folder prefixes (ending in `/`) whose
formatter-supported files must stay clean; exclusions still apply. It starts with `scripts/format/`
(the tooling, its configuration and the qualification harness). Fixtures stay excluded because they hold
deliberately dirty input. A prefix that selects no file is refused. There is no whole-repository gate:
other files are checked only when named.

`check-adopted.ps1` first proves in a temporary repository that clean adopted files pass, drift fails,
and excluded or unadopted files are neither reported nor written. That repository has its own two-prefix
scope, so enrolment never affects it. Then it checks the real adopted scope. It runs in the `verify.ps1`
policy phase, in the CI policy job, in site CI for source changes, and offline on Windows and Linux in
`format.yml`. Changes under `scripts/format/` trigger all of these.

To enrol files, make one reviewed change that applies the formatter to a folder, reviews the result by
hand (see *Limits*), and adds the folder's prefix to `adopted`. Site folders are enforced by site CI, and
the rest by repository CI.

## Pins

`config/versions.json` pins the .NET SDK, PSScriptAnalyzer (package and manifest SHA-256) and Prettier
(also locked by `config/package-lock.json`). Install restores PSScriptAnalyzer and Prettier into the
ignored `.tools/format` of the main checkout; linked worktrees share it (`-ToolRoot` overrides). It also
checks that the pinned SDK is installed; it never installs an SDK. Without the tools, every check fails
closed. Check and apply validate every pin and download nothing. The SDK is selected by a
`global.json` in a scratch folder with roll-forward disabled, so application builds keep their own SDK.
`config/csharp.editorconfig` is copied there as `.editorconfig`; the repository root has none.

## Selection and bytes

Named paths are repository-relative and case-sensitive, and must be tracked or unignored files with a supported
extension, outside `config/scope.json` exclusions (data, fixtures, lockfiles, vendor and generated files)
and not reached through a link. One refused path refuses the run. Formatting runs on copies in the
platform temp directory. Each file keeps its newline style and BOM; mixed or bare-CR endings are reported,
never fixed. Apply settles every verdict first, then replaces each file atomically, only if it is
unchanged since it was read and still reached through no link.

## Findings

| Finding | Meaning |
|---|---|
| `DRIFT` | The bytes differ from the formatter's output. The only finding apply fixes. |
| `PARSE` | The input does not parse. |
| `SEMANTICS` | The output changed a token, literal value, statement or JSON number text. |
| `SIZE` | Over 499 lines after formatting (JSON exempt). |
| `LINE-ENDINGS`, `ENCODING` | Mixed or bare-CR endings; not UTF-8. |
| `CHANGED`, `WRITE` | Apply found the file modified meanwhile, or could not replace it (including a link that appeared after selection). |

## Limits

HTML is not supported: Prettier reflows HTML text, and whether that changes what renders depends on CSS and
scripts the formatter cannot see. Formatters do not enforce AGENTS.md blank-line rules, and PSScriptAnalyzer
leaves `}else` and hashtable brace spacing as written. Review those by hand, as for HTML, AXAML and other
unsupported languages.
PSUseCorrectCasing is off because its output depends on installed commands.
