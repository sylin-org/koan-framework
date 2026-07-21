# Sylin.Koan.App technical contract

## Responsibility

`Sylin.Koan.App` is the dependency-only ASP.NET Core entry bundle. It owns no runtime types, registration hooks,
provider election, configuration keys, or compatibility registry. Its only functional statement is the composition
of `Sylin.Koan` and `Sylin.Koan.Web`.

The foundation contributes Core, Entity data, local Communication, and the JSON provider. Web contributes
controller discovery, `EntityController<T>`, health endpoints, and well-known runtime facts. Functional packages own
their activation through `KoanModule`; the bundle does not repeat those registrations.

## Version and artifact contract

The bundle has its own NBGV lineage. Its evaluated inputs include the foundation and Web projects, so a changed tested
composition mints a new App version without borrowing another package's version. Packing emits each dependency's
actual bounded compatibility range through the shared compatibility target.

The nupkg contains package metadata, its owned README, and the canonical mascot. `IncludeBuildOutput=false` and
`IncludeSymbols=false` are deliberate: there is no App assembly or PDB to consume. Final packaging verifies that the
packed dependency set equals the evaluated project graph.

## Election and limits

The bundled JSON adapter is the automatic priority-0 floor. Any referenced compatible provider with higher priority,
such as SQLite at priority 10, replaces it unless explicit source or Entity routing states otherwise. This is Data's
provider election contract, not bundle behavior.

The bundle does not imply authentication, production data guarantees, a network transport, OpenAPI, MCP, jobs, or an
external infrastructure provider. Those remain separate reference intents and independently versioned packages.
