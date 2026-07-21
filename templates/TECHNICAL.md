# Sylin.Koan.Templates technical contract

## Responsibility

This is a content-only standard NuGet `Template` package. `koan-web` expresses the App bundle plus SQLite;
`koan-console` expresses the foundation bundle plus SQLite. Generated source contains no Koan configuration because
the provider reference and SQLite's autonomous local target are sufficient intent.

## Preparation and packing

Template source contains its standard NuGet compatibility ranges directly. Both generated projects target
`[0.20.0,0.21.0)`, which accepts the guaranteed 0.20 family and rejects a future breaking 0.21 family. The
content-only project packs directly with `dotnet pack`; there is no preparation or token-replacement phase.

The packed artifact must contain both canonical `.template.config/template.json` files, `README.md`, and the exact
repository `icon.png`. It must contain no `bin`, `obj`, generated `appsettings.json`, unresolved token, runtime
dependency, or build output.

## Clean-consumer proof

The release clean room installs the exact template nupkg into an isolated `DOTNET_CLI_HOME`, creates both projects by
public short name, and restores/builds them only against the staged Koan feed plus NuGet.org dependencies. The console
proof requires visible Entity save/load/query results. The web proof starts without an injected provider setting and
requires a persisted Todo through `EntityController<Todo>`, proving SQLite's zero-configuration local election.

The templates do not own provider guarantees, host security, or application policy; they express only the shortest
honest composition and business-visible result.
