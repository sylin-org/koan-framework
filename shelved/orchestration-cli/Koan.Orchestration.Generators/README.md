# Koan.Orchestration.Generators

> ✅ Validated against manifest generation and Koan0049 diagnostics on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for the full diagnostic matrix and manifest flow.

Roslyn analyzers/source generators that emit the orchestration manifest (`__KoanOrchestrationManifest.Json`) and enforce adapter hygiene.

## Source-first use

The current repository attaches this analyzer only to projects that declare orchestration metadata.
Public-feed installation is not yet a certified coherent path. When the package wave is observed,
consume `Sylin.Koan.Orchestration.Generators` as an analyzer with `PrivateAssets="all"` and use the
version selected by that coherent release.

Annotate your adapters with `KoanServiceAttribute` (and related metadata attributes) so the generator can materialize a manifest entry.

```csharp
[KoanService(ServiceKind.Database, "postgres", "Postgres")
ContainerDefaults("postgres", Ports = new[] { 5432 }, Tag = "16.4")]
public sealed class PostgresAdapter { /* ... */ }
```

- Add the package as an analyzer reference (`PrivateAssets=all`) to avoid leaking it to consumers.
- Build the project and inspect `obj/<tfm>/__KoanOrchestrationManifest.g.cs` to verify generated content.

## Manifest output

- Generated type: `Koan.Orchestration.__KoanOrchestrationManifest` with a `Json` constant consumed by planners/CLI.
- Includes the app section when `KoanAppAttribute` or `IKoanManifest` is present.

## Diagnostics

- `Koan0049B/C` – short code validation (format + reserved names).
- `Koan0049D` – malformed `qualifiedCode`.
- `Koan0049E` – missing container image for container deployments.
- `Koan0049F` – discourage `latest` tags.
- `Koan0049G` – duplicates within the same compilation.

Treat these as build blockers (`-warnaserror`) to keep orchestration metadata healthy.

## Edge cases & tips

- Language version must support source generators (C# 9+); the repo defaults to `latestMajor` via `Directory.Build.props`.
- If incremental build caches stale manifest data, run `dotnet clean` to clear `obj/` artifacts.
- Suppress or disable specific diagnostics through `.editorconfig` (`dotnet_diagnostic.Koan0049X.severity = none`) when justified.
- Match generator package version with your Koan runtime packages to stay in sync with manifest schema.

## Related docs

- `/docs/architecture/principles.md` – orchestration design tenets referenced by diagnostics.
- [`Koan.Orchestration.Cli`](../Koan.Orchestration.Cli/README.md) – consumes the generated manifest for discovery.
- `AnalyzerReleases.Shipped.md` / `AnalyzerReleases.Unshipped.md` – diagnostic release notes.
