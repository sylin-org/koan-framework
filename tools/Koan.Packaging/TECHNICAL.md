# Koan.Packaging technical contract

## Boundary

The tool is a source-read-only repository inspector and package-contract boundary. It owns
evaluated package discovery, structural package quality reporting, product-surface compilation,
and generated-surface drift verification.

`RepositoryInspector` asks MSBuild for packability, package metadata, ProjectReferences, and standard
pack properties. It rejects missing or malformed package identity and resolves each package's nearest
ancestor NBGV `version.json`; every active package must resolve to the repository root owner.

`PackageGraph`, `PackageQualityCompiler`, and `ProductSurfaceCompiler` consume that evaluated snapshot.
They do not mutate source, Git, artifacts, or registries. Family test projects and workflows—not this
inventory tool—own behavioral execution.

## Commands

- `inventory [--output PATH]`
- `quality [--output PATH] [--markdown PATH]`
- `product-surface [--output PATH] [--markdown PATH] [--check]`

`product-surface --check` compiles the current claims and evaluated package graph, then compares the
checked-in Markdown projection without writing it. JSON is emitted on demand for release tooling.
Every package inherits the root release train; supported claims include their public dependency
closure. The evaluated package list—not claim maturity—is the release inventory.

Behavioral evidence stays with the family that owns it. Deterministic suites run through the green
ratchet, real provider boundaries run as direct workflow jobs or Forge conformance, and clean-consumer
tests prove package-only use. This keeps ordinary `dotnet test` and family-specific diagnostics as the
single execution model.

Release compilation, package-only consumer probes, staging, publication, and recovery are deliberately
outside this tool. The release path uses standard `dotnet pack` and `dotnet nuget push` in one explicit
workflow.
