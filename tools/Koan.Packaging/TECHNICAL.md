# Koan.Packaging technical contract

## Boundary

The tool is a source-read-only repository inspector and bounded test-admission boundary. It owns
evaluated package discovery, structural package quality reporting, product-surface compilation,
public API-baseline verification, and exact TRX admission verdicts.

`RepositoryInspector` asks MSBuild for packability, package metadata, ProjectReferences, and standard
pack properties. It rejects missing, malformed, or shared package identity and requires a local NBGV
`version.json`.

`PackageGraph`, `PackageQualityCompiler`, and `ProductSurfaceCompiler` consume that evaluated snapshot.
They do not mutate source, Git, artifacts, or registries. The baseline guard reads the public NuGet
version index. Admission runs one caller-selected test project in an owned system-temporary result
directory, terminates only that process tree at its deadline, and removes the directory afterward.

## Commands

- `inventory [--output PATH]`
- `quality [--output PATH] [--markdown PATH]`
- `product-surface [--output PATH] [--markdown PATH] [--check]`
- `api-baselines`
- `native-admission --base SHA --candidate SHA [--output PATH]`
- `terminal-outcomes [--final] [--output PATH]`
- `admission --id ID --project PATH --filter FILTER [--lane deterministic|native] [--phase NAME]`
- `admission-results --id ID --project PATH --filter FILTER --result PATH` (existing bounded runner integration)

An admission passes only when the process exits zero before its deadline, a readable TRX exists,
at least one selected result exists, and every result outcome is `Passed`. Failed, skipped/not-
executed, unknown, missing, or zero results fail. Family suites—not this command—continue to own
setup, readiness, behavior, teardown, and ambient-restoration assertions.

Native applicability begins from changed evaluated package owners, expands through reverse public
dependency closure, and includes changed claim documentation/evidence/test projects. Claims and
shared build/admission inputs are conservative all-claim boundaries. The command requires the base
to be an ancestor, `HEAD` to equal the supplied candidate, and the checkout to contain no tracked or
untracked changes before it emits an exact-SHA result or N/A report.

Terminal reconciliation parses the immutable 55-row table directly from ARCH-0120. Active supported
owners come only from compiled product truth; the separate bounded certificate may contain only
owners absent from the active graph with an absorbed, migrated, or retired disposition, exact public
commit, runnable commands, and evidence. Partial mode reports remaining work. Final mode fails until
the active-supported and removed sets resolve every baseline owner exactly once; future packages are
outside this fixed epic.

Release compilation, lineage, escrow, clean-room application probes, staging, promotion, and recovery
are deliberately outside this tool. The release path uses standard `dotnet pack` and
`dotnet nuget push` in one explicit workflow.
