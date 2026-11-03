# Happy Photon Engineering Standard

Adapted from PC Tools' `docs/ENGINEERING-STANDARD.md`; ENG identifiers retain
that source's meanings. This local standard governs Happy Photon code, tests
and tooling. Adopted guidance is not a claim that existing code conforms.
Existing code and tooling migrate only through explicitly scoped changes.

## Ownership and scope (ENG-01)

[AGENTS.md](../AGENTS.md) owns agent instructions, safety boundaries, planning,
measured-change gates, code spacing and verification. Architecture and pipeline
contracts remain in [ARCHITECTURE.md](ARCHITECTURE.md) and
[pipeline/OVERVIEW.md](pipeline/OVERVIEW.md); feature specs own planned work.
Preserve those contracts. Resolve conflicts explicitly; the owner's explicit
instructions govern. Record accepted exceptions and a short reason in the
relevant component documentation.

PC Tools' operational module layout, result envelope, exit codes, report shell,
notification system and Windows-specific storage paths are not adopted.
Happy Photon retains its MVVM layout, application storage and cross-platform
contracts. Adoption does not authorize behavior, schema or pipeline changes.

## Small, readable changes (ENG-03, ENG-07)

Make the smallest clear change that solves the request. Avoid unrelated cleanup,
speculative features and unnecessary abstractions. Use descriptive names and
ordinary constructs, with blank lines between members and logical steps as
specified in AGENTS.md. Do not stack statements, strip useful comments or hide
source in strings to evade checks. Apply these rules to new and changed code;
do not reformat untouched code. PowerShell module exports use approved verbs
(`Get-Verb`).

## Formatting (ENG-08)

Use standard formatters with pinned versions and repository settings, scoped
to the change: `dotnet format whitespace` for C#, PSScriptAnalyzer
`Invoke-Formatter` for PowerShell, and Prettier for JS/TS, HTML, CSS and JSON.
Qualify and pin each formatter before making its output a repository gate;
formatter installation or a repository-wide rewrite is a separate scope.
Until a formatter is qualified, follow the repository's documented style and
report manual formatting. AXAML, batch and task XML likewise use documented
style unless a formatter is explicitly qualified. Preserve intentional line
endings and script runtime compatibility; do not import PC Tools' global CRLF
policy into cross-platform files.

## File size and embedded source (ENG-10)

Hand-authored source files, including tests, entrypoints and source templates,
have at most 499 physical lines after formatting. Docs, generated files, data,
schemas and fixtures are exempt. Split along responsibilities, never into
numbered fragments or one file per function; preserve the repository's flat
services and ViewModel partial ownership. Never compress code to meet the limit.
Keep another language's source out of PowerShell strings; place C# in `.cs`
files loaded with `Add-Type -Path`, including test fakes.

The current `scripts/check-source-lines.ps1` enforces the limit for C# and
AXAML. Review other source types manually until enforcement is extended in a
separate scoped change; a passing policy run does not establish full conformance.

## Isolated tests and review (ENG-12, ENG-15)

Tests and their tools must not alter the user's persistent environment.
Use disposable scratch roots and injected dependencies for file, catalog,
cloud, network and process effects. Never use the user's originals, live catalog,
cloud placeholders or installed tasks as test fixtures. Live qualification must
be explicitly scoped and preserve AGENTS.md's source-read approval rules.
Do not change expected results merely to make a failing test pass; establish
that the intended contract changed and retain meaningful regression coverage.

Review the final diff and safety assumptions for defects and simplification;
explain declined findings. Run the checks required by AGENTS.md. Passing tests
never authorize elevated or destructive operations. Review depth should match
the change's risk; this standard does not require a separate agent for every edit.

## Commits (ENG-16)

Use coherent commits with one imperative subject line and no body, emoji or
attribution trailers. Follow AGENTS.md's squash-before-merge rule without
rewriting published history; squash at merge when branch commits are published.
PC Tools' Git hook paths and automatic commit-and-push rule are not adopted.

## Applicable operational principles

For new or changed tooling, apply PC Tools' module safety principles where
relevant: bound external command time and output, distinguish unknown or partial
results from success or zero, validate inputs before side effects, and publish
shared files atomically. Keep presentation failures distinct from operation
outcomes. Version persisted contracts and validate what readers consume according
to the owning Happy Photon contract; do not retrofit a generic result envelope
or stricter configuration policy onto existing formats through this standard.
