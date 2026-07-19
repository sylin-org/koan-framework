# Sylin.Koan.Web.Extensions technical contract

## Responsibility

This package owns optional Entity-oriented HTTP realizations above `Sylin.Koan.Web`:

- `[RestEntity]` discovery and a concrete closure of `EntityController<TEntity,TKey>`;
- generic controller materialization and route conventions;
- moderation and audit controller bases and request contracts; and
- named capability-policy providers that participate in Koan Web's shared `IAuthorize` ladder when configured.

It does not own MVC hosting, the base Entity CRUD service, authentication, the shared gate/constrain/project access
model, data-provider election, or adapter semantics.

## Activation and host ownership

`WebExtensionsModule` is the sole module activation owner. It adds this assembly as one MVC application part,
contributes one feature provider and route convention, and discovers `[RestEntity]` declarations from Koan's compiled
assembly closure.

`GenericControllerRegistry` is created per `IServiceCollection`. The module, registration extensions, feature provider,
and route convention share that instance. No process-static controller registry exists, so parallel or sequential hosts
cannot inherit each other's projections.

An explicit concrete `EntityController<TEntity,TKey>` found in the same closure suppresses the terse controller for
that Entity. Re-registering the same generic realization and route is idempotent. Registering the same realization at a
different route throws and directs the application to an explicit controller.

## Authorization

`[RestEntity]` adds no access metadata. Base CRUD runs through `Sylin.Koan.Web`'s shared Entity endpoint service and
gate/constrain/project model.

Capability controller actions require ASP.NET authentication and carry a `RequireCapability` action. When the
application calls `AddKoanAuthorization(...)` or `AddCapabilityAuthorization(...)`, `PolicyAuthorizationProvider`
maps those actions through Entity-specific settings, defaults, and the declared default behavior. RBAC and other
providers remain ordered participants in the same `IAuthorize` ladder.

## Data behavior

The capability controllers use Entity/data partition scopes for draft, submitted, approved, denied, and audit
sets. Paging limits reuse Koan Web defaults. Backend support, atomicity, and cost remain adapter-specific; the package
does not add a transaction coordinator or streaming fallback.

## Deliberate non-guarantees

- no automatic moderation/audit exposure from a package reference alone;
- no second soft-delete persistence model; `Sylin.Koan.Data.SoftDelete` owns that Data semantic;
- no multi-route generic controller realization;
- no compliance-grade append-only audit store or retention engine;
- no cross-partition atomic workflow guarantee; and
- no replacement for domain-named workflows when the generic lifecycle is insufficient.
