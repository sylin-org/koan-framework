---
type: GUIDE
domain: framework
title: "Koan documentation"
audience: [developers, operators, architects, ai-agents]
status: current
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: verified
  scope: greenfield capability curriculum and canonical public navigation
---

# Koan documentation

Start with the business capability your application needs. Each pillar gives you the smallest current
application expression, the guarantee it creates, provider and configuration choices, the corrective
failure when that guarantee cannot be met, and links to package-specific or operational detail.

If Koan is new to you, install `Sylin.Koan.Templates` and
[build the first application](getting-started/quickstart.md). The root [README](../README.md) owns
Koan's product promise and shortest meaningful result; the generated product surface owns maturity.

## Choose your path

| You want to… | Start here |
|---|---|
| Build a new application | [First application](getting-started/quickstart.md) |
| Add Koan to an existing ASP.NET Core application | [Incremental adoption](getting-started/adopt-existing-app.md) |
| Evaluate fit, responsibilities, and deployment shapes | [Architecture at a glance](architecture/index.md) |
| Review security or production behavior | [Identity and isolation](reference/identity/index.md) and [testing and operations](reference/operations/index.md) |
| Extend a capability or contribute a provider | [Contributing](../CONTRIBUTING.md) and [engineering workbooks](engineering/README.md) |
| Orient a coding agent | [Agent retrieval map](../llms.txt) |

## Capability pillars

| Business need | Pillar | First public concept |
|---|---|---|
| Understand composition and provider election | [Foundation and composition](reference/core/index.md) | `AddKoan()` and referenced modules |
| Persist, query, relate, page, or stream business state | [Data](reference/data/index.md) | `Entity<T>` statics and instance verbs |
| Expose the same model through HTTP | [Web](reference/web/index.md) | `EntityController<T>` |
| Authenticate, authorize, and isolate application data | [Identity and isolation](reference/identity/index.md) | provider configuration, `[Access]`, and tenant context |
| Run durable work and communicate Entity intent | [Work and communication](reference/work/index.md) | `IKoanJob<T>`, `.Events`, and `.Transport` |
| Cache state, store files, and serve derivatives | [State and content](reference/state-content/index.md) | `[Cacheable]`, storage bindings, and media recipes |
| Add AI, vector, and search behavior | [Intelligence](reference/ai/index.md) | AI sources, `[Embedding]`, and vector search |
| Expose governed tools and resources to agents | [Agents](reference/agents/index.md) | `[McpEntity]` and `[McpTool]` |
| Reconcile records into trusted Entities | [Canon](reference/canon/index.md) | canonical pipelines and contributors |
| Test, inspect, and operate the application | [Testing and operations](reference/operations/index.md) | conformance, health, facts, and diagnostics |

## Product truth

- [Product and package surface](reference/product-surface.md) is the sole support-maturity authority.
- [Package quality](reference/package-quality.md) reports structural package facts, not maturity.
- Package READMEs own provider-specific installation, configuration, and limits.
- Package technical companions own internals, lifecycle, extension points, and operational detail.
- [Graduated samples](../samples/README.md) demonstrate complete business journeys without redefining
  pillar contracts.

## Operate and evaluate

- [Architecture at a glance](architecture/index.md)
- [Troubleshooting](support/troubleshooting.md)
- [HTTP API reference](reference/web/http-api.md)
- [Glossary](reference/glossary.md)
- [Product constitution](architecture/product-constitution.md)
- [Entity semantics contract](architecture/entity-semantics-contract.md)
- [Framework principles](architecture/principles.md)

## Documentation boundary

Current public guidance is reachable from this curriculum, an owning package, or a graduated sample.
Initiatives, assessments, proposals, implementation plans, and archives are engineering evidence—not
alternate application patterns. ADRs are dated decision history and may describe an earlier system.
