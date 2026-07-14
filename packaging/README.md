# Koan bundles

This directory owns Koan's dependency-only convenience bundles:

- `Sylin.Koan` — the tested foundation: Core, Data abstractions/core, and the JSON connector.
- `Sylin.Koan.App` — the foundation plus controller-based ASP.NET Core integration.

Each bundle is a normal SDK package project with its own NBGV version. ProjectReferences preserve the
independently evaluated version and bounded compatibility range of every member. Bundle path filters
include their composition, so changing a member advances the bundle without forcing that bundle's
version onto unrelated packages.

The [release compiler](../tools/Koan.Packaging/README.md) discovers, plans, packs, proves, and publishes
these projects with the rest of the package graph. Do not add tokenized nuspecs or pack bundles through
a separate path.

```powershell
dotnet pack packaging/Sylin.Koan/Sylin.Koan.csproj -c Release -p:PublicRelease=true
dotnet pack packaging/Sylin.Koan.App/Sylin.Koan.App.csproj -c Release -p:PublicRelease=true
```
