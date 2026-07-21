# Sylin.Koan technical contract

## Responsibility

`Sylin.Koan` is the dependency-only foundation bundle. It composes `Sylin.Koan.Core`, Communication, Data
Abstractions, Data Core, and the JSON connector. It owns no runtime types, module, provider election, configuration
key, or compatibility registry; each functional package activates and reports itself.

The JSON provider is included so the foundation has a meaningful zero-infrastructure result. Its priority-0
`IsAutomaticFloor` role is deliberately replaceable. A higher-priority referenced provider or explicit source/Entity
route changes Data election without changing the bundle or domain code.

## Version and artifact contract

The bundle owns an independent NBGV version. Its evaluated lineage includes the composed package inputs; changing any
tested component therefore mints a new foundation identity. Packing converts each direct `ProjectReference` to that
component's actual bounded compatibility range through `build/compat-ranges.targets`.

The nupkg contains only dependency metadata, its owned README, and the canonical mascot. It intentionally emits no
runtime assembly, PDB, or symbol package. Final package verification requires the packed dependency set and floors to
match the evaluated release manifest.

## Boundaries

This bundle does not include ASP.NET Core projection, SQLite, a network transport, authentication, MCP, jobs, AI, or
operator tooling. Those are separate reference intents. It also does not upgrade JSON's bounded local-file behavior
into a production data claim; provider guarantees remain owned by the elected connector.
