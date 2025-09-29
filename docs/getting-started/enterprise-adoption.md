---
type: GUIDE
domain: core
title: "Enterprise Adoption Guide"
audience: [architects, developers, ai-agents]
last_updated: 2025-02-14
framework_version: v0.2.18
status: current
validation:
  date_last_tested: 2025-02-14
nav: true
---

# Enterprise Adoption Guide: Strategic Framework Integration

## Contract

- **Inputs**: An existing or planned .NET service portfolio, baseline CI/CD infrastructure, and leadership sponsorship for a pilot.
- **Outputs**: A phased rollout plan, governance checkpoints, and KPIs that demonstrate Koan's value for your organization.
- **Error Modes**: Incomplete provider capability validation, siloed security sign-off, or teams skipping reference patterns in favor of legacy stacks.
- **Success Criteria**: Pilot services deliver to production with Koan defaults, governance artifacts pass review, and KPIs show measurable velocity improvements.

### Edge Cases

- Heavily regulated environments may require additional compliance mapping—loop in governance before pilots begin.
- Brownfield migrations with tightly coupled monoliths may need interim adapters; plan capacity for integration glue.
- AI or vector features depend on licensed providers—flag budget approvals early.
- Multi-region deployments demand per-region adapter configuration; keep residency requirements in the rollout workbook.

---

## 1. Executive Summary

Koan lets small teams ship sophisticated, AI-native services with governance intact. The framework amplifies productivity through the `Entity<T>` pattern, produces deployment artifacts automatically, and keeps your architecture portable across providers.

| Enterprise Challenge | Koan Response |
| --- | --- |
| Large teams required for advanced features | Lean teams deliver via consistent patterns |
| Prototype-to-production gap | Same code path from local to prod |
| Vendor lock-in risk | Provider transparency across data, messaging, AI |
| Governance overhead | Generated Compose profiles, health checks, and observability hooks |

---

## 2. Adoption Strategies

### Pilot (2–4 weeks)

1. **Bootstrap** a service with REST, messaging, and AI in the first sprint.
2. **Instrument** Flow hooks to prove event-driven automation.
3. **Generate** deployment assets (`koan export compose`) and review with ops.
4. **Measure** velocity vs. baseline and share outcomes with steering teams.

### Greenfield Build

- Use `Entity<T>` for every domain aggregate from day one.
- Declare dependencies through package references (`Koan.Data.Postgres`, `Koan.AI.OpenAI`, etc.) to maintain "Reference = Intent" auditability.
- Keep provider selection per environment in configuration; the code remains unchanged across dev/stage/prod.

### Brownfield Enhancement

- Stand up Koan sidecars that publish and consume events alongside existing services.
- Translate legacy payloads into Koan entities, then leverage AI or Flow without disturbing the upstream system.
- Incrementally retire legacy endpoints as Koan controllers take over.

---

## 3. Governance Integration

### Architecture Review Checklist

- Entities inherit from `Entity<T>` and controllers from `EntityController<T>` unless a justified exception is documented.
- Dependencies are declared via Koan packages; manual DI registrations are limited to app-specific services.
- Flow handlers cover mission-critical automation paths and log structured audit events.
- Health endpoints (`/api/health/*`) are exposed and monitored.

### Security & Compliance

```csharp
Flow.OnUpdate<CustomerRecord>(async (record, previous) =>
{
    if (record.Ssn != previous.Ssn)
    {
        await new AuditEvent
        {
            Actor = record.UpdatedBy,
            Action = "SensitiveFieldChanged",
            Resource = record.Id,
            OccurredAt = DateTime.UtcNow
        }.Send();
    }

    return UpdateResult.Continue();
});
```

- Map Koan health endpoints into your existing uptime SLA monitors.
- Use provider-specific adapters (`[SourceAdapter("eu-postgres")]`) to respect data residency boundaries.
- Align secrets with your vault strategy; Koan's configuration helpers support environment-first resolution.

---

## 4. Team Enablement

| Week | Focus | Outcomes |
| --- | --- | --- |
| 1 | Entity pattern & controllers | CRUD API live, health checks wired |
| 2 | Messaging + Flow | Event automation logging in lower environments |
| 3 | AI & vector | Semantic search or chat endpoint validated |
| 4 | Production hardening | Compose export reviewed, observability dashboards baselined |

Create a guild of "Koan champions" who review architecture proposals, run office hours, and document organization-specific recipes.

---

## 5. Metrics & KPIs

- **Velocity**: Time from ticket to prod should drop; target <1 week for standard features.
- **Operational Burden**: Track incidents tied to misconfiguration—expect a decline after adopting generated artifacts.
- **Adoption Health**: Count services using Koan Flow, AI, and multi-provider features to measure maturity.
- **Developer Sentiment**: Quarterly survey on onboarding time and satisfaction with the framework.

---

## 6. Next Steps

- Start with the [quickstart](./quickstart.md) to ground teams in the basics.
- Share the [complete getting started guide](./guide.md) with squads taking ownership of new services.
- Compare Koan to adjacent stacks using the [architecture comparison](../architecture/comparison.md).
- Coordinate distributed deployments with the [ASPIRE integration guide](../ASPIRE-INTEGRATION.md).

---

**With Koan, enterprise ambition no longer demands enterprise headcount—deliver more by standardizing on the patterns that scale.**
