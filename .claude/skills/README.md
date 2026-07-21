# Koan agent skills

These focused guides help coding agents apply Koan's current application language. They are indexes,
not a second API reference: each skill points to the package/reference owner, a runnable sample, and
the generated maturity boundary.

## How to use them

Load the smallest skill that matches the user's business intent. Begin with standard .NET and Koan's
public Entity language; add framework-specific concepts only when the referenced capability earns
them. A package being present in the repository does not make its behavior supported—check the
[product surface](../../docs/reference/product-surface.md).

Every recommended web host uses the same four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

## Supported 0.20 paths

| Skill | Use it for |
|---|---|
| [koan-quickstart](koan-quickstart/SKILL.md) | Create the first template application and reach one Entity result. |
| [koan-entity-first](koan-entity-first/SKILL.md) | Model business state with `Entity<T>` and its canonical verbs. |
| [koan-bootstrap](koan-bootstrap/SKILL.md) | Understand `AddKoan()`, Reference = Intent, and `KoanModule`. |
| [koan-debugging](koan-debugging/SKILL.md) | Read startup, health, facts, lock drift, and corrective failures. |
| [koan-data-modeling](koan-data-modeling/SKILL.md) | Model keys, aggregates, relationships, lifecycle, and value objects. |
| [koan-caching](koan-caching/SKILL.md) | Apply Entity cache semantics and inspect topology limits. |
| [koan-jobs](koan-jobs/SKILL.md) | Express durable Entity-owned work, retry, progress, and schedules. |
| [koan-web](koan-web/SKILL.md) | Project Entities and business actions through ASP.NET Core. |
| [koan-communication](koan-communication/SKILL.md) | Use Entity Events, Transport, channels, local settlement, and RabbitMQ carriage. |
| [koan-auth](koan-auth/SKILL.md) | Compose authentication, durable identity, authorization, trust, and tenant-aware identity. |
| [koan-tenancy](koan-tenancy/SKILL.md) | Apply tenant isolation and host-authorized administration. |
| [koan-mcp-integration](koan-mcp-integration/SKILL.md) | Project governed Entity/tool surfaces through MCP. |
| [koan-observability](koan-observability/SKILL.md) | Add one OpenTelemetry pipeline and application health contributions. |

## Available capabilities with narrower maturity

These skills explain real current APIs, but their packages or provider combinations are not part of
the complete supported 0.20 closure. State the limitation and check the generated claim before
recommending one for production use.

| Skill | Use it for |
|---|---|
| [koan-ai](koan-ai/SKILL.md) | Prompt, model, and AI provider semantics. |
| [koan-vector](koan-vector/SKILL.md) | Vector indexing/search and provider-specific limits. |
| [koan-media](koan-media/SKILL.md) | Entity-backed media recipes and HTTP rendering. |
| [koan-storage](koan-storage/SKILL.md) | Named storage profiles and provider boundaries. |
| [koan-multi-provider](koan-multi-provider/SKILL.md) | Deliberate provider/source selection without claiming parity. |
| [koan-performance](koan-performance/SKILL.md) | Paging, streaming, batching, and measured provider-aware tuning. |

## Deployment boundary

[koan-orchestration](koan-orchestration/SKILL.md) explains the current non-package boundary: ordinary
.NET, Aspire, Compose, Docker/Podman, Kubernetes, or another platform owns topology. Koan connectors
consume standard endpoints and configuration; the shelved bespoke CLI/Aspire bridge is not a 0.20
product surface.

## Maintenance contract

- Directory name and frontmatter `name` agree.
- The description is the activation surface.
- A canonical example is compile-checked where the skill teaches code.
- Relative links resolve and package IDs use `Sylin.Koan.*`.
- Current instructions use generated module composition and `await app.RunAsync()`.
- Skills link to canonical product docs instead of duplicating full API inventories.

Run `pwsh scripts/skills-lint.ps1 -Strict` and `pwsh scripts/validate-code-examples.ps1` after changing
skills. The public-document gate also treats every tracked skill asset as public narrative.

Aligned with the Koan 0.20 preview on 2026-07-19.
