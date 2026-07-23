# Security Policy

## Supported versions

Happy Photon is preparing its first public preview.

| Version | Status |
| --- | --- |
| `main` | Pre-release security fixes |
| `0.1.x` | Supported after publication |

## Reporting a vulnerability

Use GitHub's private vulnerability reporting for
`seasalim/happy-photon`. Do not open a public issue for a suspected
vulnerability.

Include:

- the affected version and operating system;
- a concise reproduction;
- the expected and observed behavior;
- the security or privacy impact; and
- any suggested mitigation.

Do not include real photographs, catalog databases, tokenized Agent URLs,
credentials, or other personal data. Use a minimal synthetic reproduction.

The maintainer will acknowledge a complete report as soon as practical,
normally within seven days. Public disclosure will be coordinated after a fix
or mitigation is available.

## Security boundaries

Agent access is off by default, binds only to localhost, and uses a random
token in its URL. The interface exposes metadata and locally computed numeric
statistics, not image pixels or arbitrary file contents. Agent-visible data
may still be transmitted by the MCP client or model selected by the user.

Ratings, flags, presets, edit settings, and exports requested through the
agent interface take effect immediately. Connect only trusted clients and use
a scratch copy when evaluating automated workflows.
