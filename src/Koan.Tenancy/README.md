# Sylin.Koan.Tenancy

Tenant isolation as a referenced application capability: ordinary Entities become tenant-scoped while application
code keeps the same business shape.

## Install

```powershell
dotnet add package Sylin.Koan.Tenancy
```

Keep the ordinary Koan bootstrap:

```csharp
builder.Services.AddKoan();
```

No `AddTenancy()`, repository wrapper, tenant property, or per-pillar registration is required.

## Meaningful use

```csharp
public sealed class Invoice : Entity<Invoice>
{
    public decimal Total { get; set; }
}

using (Tenant.Use("acme"))
{
    await new Invoice { Total = 120m }.Save();
    var invoices = await Invoice.All();
}
```

The ambient tenant automatically segments participating Data, Cache, Storage, Communication, and durable-context
capabilities. Each active pillar compiles the same `tenant` meaning into its own runtime chokepoint.

Use `[HostScoped]` for genuinely global control-plane Entities:

```csharp
[HostScoped]
public sealed class PlanDefinition : Entity<PlanDefinition>;
```

## Runtime behavior

- Development resolves an otherwise-unscoped operation to the stable local tenant `dev`.
- Non-Development hosts default to Closed posture: a tenant-scoped operation without a trusted scope fails with a
  corrective error before provider work.
- `Tenant.None()` establishes explicit host intent. It allows host-scoped models; it does not turn a tenant Entity
  into host data.
- Jobs and other Core context consumers can capture and restore the tenant without application carrier code.
- A provider that cannot enforce the compiled segmentation guarantee rejects the operation rather than degrading.

Override posture only for focused verification:

```json
{
  "Koan": {
    "Tenancy": {
      "Posture": "Closed"
    }
  }
}
```

Open posture outside Development refuses startup.

## Durable control plane

`TenantRecord` stores the stable tenant id, display name, and optional routing code. `Membership` stores one
deterministic `(tenant, subject)` seat and its tenant roles. `TenantAuditEntry` is the integrity record used by the
supported Web administration projection.

Reference `Sylin.Koan.Identity.Tenancy` when authenticated HTTP requests should resolve carriers through active
durable Identity membership. Reference `Sylin.Koan.Tenancy.Web` for the operator registry/membership UI and API.

## Boundaries

- Core Tenancy has no HTTP resolver prerequisite; workers and non-Web hosts use explicit or captured scope.
- Tenant routing codes written directly through Entity APIs can conflict. Inbound resolution fails closed on
  ambiguity; the supported Web administration path rejects duplicates.
- Tenant invitation, tenant-status enforcement, product-data erasure, verified custom domains, and operator act-as
  are not current guarantees.
- Audit entries are ordinary durable Entities written by the supported mutation chokepoint, not an append-only or
  externally attested ledger.

See [TECHNICAL.md](TECHNICAL.md) and the public [tenancy guide](../../docs/guides/tenancy-howto.md).
