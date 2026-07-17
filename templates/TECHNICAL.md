# Sylin.Koan.Templates technical contract

## Responsibility

This is a content-only standard NuGet `Template` package. `koan-web` expresses the App bundle plus SQLite;
`koan-console` expresses the foundation bundle plus SQLite. Generated source contains no Koan configuration because
the provider reference and SQLite's autonomous local target are sufficient intent.

The package's three suppressed `ProjectReference` items express release impact for the two bundles and SQLite. They
must never appear as runtime dependencies in the template nupkg.

## Preparation and packing

Template source contains compatibility-range tokens, not a version registry or user prompt. `Koan.Packaging` resolves
each floor from the selected release manifest, compiles Koan's closed-open compatibility band, copies only template
source to a temporary root, and supplies that root to `dotnet pack`. Direct packing fails before artifact emission
because the unprepared source cannot prove those release facts.

The packed artifact must contain both canonical `.template.config/template.json` files, `README.md`, and the exact
repository `icon.png`. It must contain no `bin`, `obj`, generated `appsettings.json`, unresolved token, runtime
dependency, or build output.

## Clean-consumer proof

The release clean room installs the exact template nupkg into an isolated `DOTNET_CLI_HOME`, creates both projects by
public short name, and restores/builds them only against the staged Koan feed plus NuGet.org dependencies. The console
proof requires visible Entity save/load/query results. The web proof starts without an injected provider setting and
requires a persisted Todo through `EntityController<Todo>`, proving SQLite's zero-configuration local election.

Template package, entry-bundle, SQLite, and generated-application failures block the wave before publication. The
templates do not own provider guarantees, host security, or application policy; they prove only the shortest honest
composition and business-visible result.
