# Koan bundles

This directory owns Koan's dependency-only convenience bundles:

- `Sylin.Koan` — the tested foundation: Core, Data abstractions/core, and the JSON connector.
- `Sylin.Koan.App` — the foundation plus controller-based ASP.NET Core integration.

Each bundle is a normal SDK package project. It inherits the repository's shared train version, and
its ProjectReferences emit the same bounded compatibility range as every other Koan package.

The [packaging tool](../tools/Koan.Packaging/README.md) discovers these projects in the active release
inventory. Do not add tokenized nuspecs or pack bundles through a separate path.

```powershell
dotnet pack packaging/Sylin.Koan/Sylin.Koan.csproj -c Release -p:PublicRelease=true
dotnet pack packaging/Sylin.Koan.App/Sylin.Koan.App.csproj -c Release -p:PublicRelease=true
```
