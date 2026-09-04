# Security policy

Koan ships to nuget.org as the `Sylin.Koan*` package family and is consumed by applications that
trust it with their data plane, background work, and agent surfaces. Security reports are taken
seriously and handled quietly.

## Supported versions

| Version | Supported |
|---|---|
| 1.x (latest patch on nuget.org) | yes |
| older 1.0.x patches | best effort — the train moves fast; update first |

Koan is pre-announcement; there is no long-term-support fork. The stabilization train is the
support surface.

## Reporting a vulnerability

**Do not open a public issue for a security report.**

Use [GitHub private vulnerability reporting](https://github.com/sylin-org/koan-framework/security/advisories/new)
on this repository. Include:

- the resolved `Sylin.*` package versions (`dotnet list package`);
- the capability involved (data connector, Jobs, MCP, Web/Auth, …);
- a minimal reproduction and the composition facts (`/.well-known/Koan/facts`) if runtime
  behavior is involved.

You will get an acknowledgment, a triage decision, and a coordinated fix-and-release plan.
We credit reporters in the release notes by default — say so if you prefer otherwise.

## Scope notes

- **Supply chain**: releases are built only by the GitHub Actions `Release` workflow from the
  fast-forwarded `main` commit and carry nuget.org repository signatures.
  Report anything that suggests a build or publish path outside that boundary.
- **Agent surfaces** (MCP): advertisement is enforcement — a caller's tool list contains only
  what its identity may use. Reports about access-rule bypass on MCP or Web surfaces are high
  priority.
