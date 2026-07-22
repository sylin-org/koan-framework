# Koan.Packaging technical contract

## Boundary

The tool is a source-read-only repository inspector and package-contract boundary. It owns
evaluated package discovery, structural package quality reporting, product-surface compilation,
generated-surface drift verification, and public API-baseline verification.

`RepositoryInspector` asks MSBuild for packability, package metadata, ProjectReferences, and standard
pack properties. It rejects missing, malformed, or shared package identity and requires a local NBGV
`version.json`.

`PackageGraph`, `PackageQualityCompiler`, and `ProductSurfaceCompiler` consume that evaluated snapshot.
They do not mutate source, Git, artifacts, or registries. The baseline guard reads the public NuGet
version index. Family test projects and workflows—not this inventory tool—own behavioral execution.

## Commands

- `inventory [--output PATH]`
- `quality [--output PATH] [--markdown PATH]`
- `product-surface [--output PATH] [--markdown PATH] [--check]`
- `api-baselines`

`product-surface --check` compiles the current claims and evaluated package graph, then compares both
canonical generated outputs without writing them. Supported claims require 0.20 version intent and a
supported public dependency closure. `api-baselines` independently requires package validation for
supported assembly owners once a public baseline exists; never-published packages remain explicit
first-publication cases.

Behavioral evidence stays with the family that owns it. Deterministic suites run through the green
ratchet, real provider boundaries run as direct workflow jobs or Forge conformance, and clean-consumer
tests prove package-only use. This keeps ordinary `dotnet test` and family-specific diagnostics as the
single execution model.

Release compilation, lineage, escrow, clean-room application probes, staging, promotion, and recovery
are deliberately outside this tool. The release path uses standard `dotnet pack` and
`dotnet nuget push` in one explicit workflow.
