# Sylin.Koan.Web.Auth.Abstractions

Inert contracts shared by Koan authentication modules. Application projects normally reference a functional
authentication package or provider connector instead of this package directly.

## Install

```powershell
dotnet add package Sylin.Koan.Web.Auth.Abstractions
```

## Meaningful use

Reference this package when authoring a reusable module that implements an auth lifecycle handler, identity store,
current-user projector, or authentication provider definition without activating Koan's Web Auth runtime.

## Boundaries

- Contains no `KoanModule` and activates no authentication, middleware, controllers, or provider.
- Does not issue or validate tokens.
- Functional applications should reference `Sylin.Koan.Web.Auth` or a provider connector.

See [`TECHNICAL.md`](TECHNICAL.md) for ownership and dependency rules.
