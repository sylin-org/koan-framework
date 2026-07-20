# Koan.Packaging technical contract

## Boundary

The tool is a read-only repository inspector. It owns evaluated package discovery, structural package
quality reporting, and product-surface compilation.

`RepositoryInspector` asks MSBuild for packability, package metadata, ProjectReferences, and standard
pack properties. It rejects missing, malformed, or shared package identity and requires a local NBGV
`version.json`.

`PackageGraph`, `PackageQualityCompiler`, and `ProductSurfaceCompiler` consume that evaluated snapshot.
They do not mutate source, Git, artifacts, registries, or remote services.

## Commands

- `inventory [--output PATH]`
- `quality [--output PATH] [--markdown PATH]`
- `product-surface [--output PATH] [--markdown PATH]`

Release compilation, lineage, escrow, clean-room application probes, staging, promotion, and recovery
are deliberately outside this tool. The release path uses standard `dotnet pack` and
`dotnet nuget push` in one explicit workflow.
