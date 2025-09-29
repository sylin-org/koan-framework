---
type: EXAMPLES
domain: core
title: "Entity Pattern Recipe Catalog"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2025-09-28
framework_version: v0.6.2
validation:
  date_last_tested: 2025-09-28
  status: verified
  scope: docs/examples/entity-pattern-recipes.md
---

# Entity Pattern Recipe Catalog

## Contract

- **Inputs**: Koan entities defined with `Entity<T>`, at least one storage adapter, and the [Data Modeling Playbook](../guides/data-modeling.md) on hand.
- **Outputs**: Repeatable recipes that demonstrate how the entity pattern spans CRUD, messaging, Flow automation, and AI enrichment.
- **Error Modes**: Forgetting capability checks before enabling vectors, omitting lifecycle protections when applying soft deletes, or duplicating query logic outside the entity.
- **Success Criteria**: The same entity model powers persistence, events, AI, and APIs without extra repositories.

### Edge Cases

- **Provider swaps** – re-run capability checks when changing adapters (SQL ➜ NoSQL ➜ vector stores).
- **Backfills** – plan Flow pipelines for historical data so AI/vector indexes stay aligned.
- **Event storms** – throttle Flow handlers when emitting follow-on events from lifecycle hooks.
- **Cross-pillar reuse** – document which static helpers Web, Flow, and Messaging depend on.

---

## Quick Reference Map

| Scenario                 | Start here                                                                                     | Combine with                                                                    |
| ------------------------ | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| CRUD service with paging | [Stage 1 – CRUD Backbone](../guides/data-modeling.md#stage-1--crud-backbone)                   | [API Delivery Playbook](../guides/building-apis.md)                             |
| Event-driven projections | [Stage 2 – Event-Driven Messaging](../guides/data-modeling.md#stage-2--event-driven-messaging) | [Semantic Pipelines Playbook](../guides/semantic-pipelines.md)                  |
| AI-assisted workflows    | [Stage 3 – AI-Enriched Domain](../guides/data-modeling.md#stage-3--ai-enriched-domain)         | [AI Integration Playbook](../guides/ai-integration.md)                          |
| Governance / audit       | [Soft Delete with Guardrails](../guides/data-modeling.md#soft-delete-with-guardrails)          | [Koan Troubleshooting Hub](../support/troubleshooting.md#flow--pipeline-health) |

---

## Recipe Summaries

### CRUD Backbone

Start with the lightest possible entity: defaults for required fields, minimal behavior, and static helpers for your most common queries.

```csharp
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }

    public static Task<Product?> ByName(string name, CancellationToken ct = default) =>
        Query().Where(p => p.Name == name).FirstOrDefaultAsync(ct);
}
```

**Checklist**

- Use constructor defaults instead of null checks downstream.
- Add paging helpers (`FirstPage`, `Page`) before exposing collection endpoints.
- Keep mutations inside instance methods (`ApplyDiscount`, `Archive`).

### Event-Driven Messaging

Reuse entity lifecycle hooks to emit domain events and fan them out with Flow. One pattern, multiple transports.

```csharp
public class StockDepleted : Entity<StockDepleted>
{
    public string ProductId { get; set; } = "";
    public int LastQuantity { get; set; }
}

public static class InventoryPipeline
{
    public static void Configure(FlowPipeline flow) =>
        flow.OnUpdate<Product>(async (updated, previous) =>
        {
            if (updated.Quantity > 0 || previous.Quantity == 0) return UpdateResult.Continue();

            await new StockDepleted
            {
                ProductId = updated.Id,
                LastQuantity = previous.Quantity
            }.Send();

            return UpdateResult.Continue();
        });
}
```

**Checklist**

- Emit events from lifecycle hooks rather than controllers.
- Route notifications through Flow or Messaging adapters instead of custom dispatchers.
- Attach payload transformers in Web to surface event outcomes consistently.

### AI-Enriched Domain

Blend vector persistence with entity saves so semantic search is always in sync.

```csharp
[DataAdapter("vector-store")]
public class DocumentIndex : Entity<DocumentIndex>
{
    public string DocumentId { get; set; } = "";
    public string Summary { get; set; } = "";

    [VectorField]
    public float[] Embedding { get; set; } = [];
}
```

**Checklist**

- Generate embeddings during writes or via Flow background jobs.
- Validate dimension sizes before saving vectors.
- Capture provider/model IDs in configuration for quick swaps.

### Extended Moves

- **Soft delete** – expose `Active()` helpers that respect `IsDeleted` flags.
- **Audit trail** – persist serialized snapshots in an `AuditLog` entity.
- **Projection hubs** – publish denormalized views via Flow and expose them through Web.

Each extended move is implemented step-by-step inside the [Advanced Patterns](../guides/data-modeling.md#advanced-patterns) section of the playbook.

---

## Cross-Pillar Checklist

- [ ] Data: Static helpers cover CRUD + streaming requirements.
- [ ] Web: Controllers rely on entity statics and payload transformers.
- [ ] Flow: Pipelines subscribe to lifecycle events without duplicating queries.
- [ ] AI: Embedding pipelines reuse the same entity definitions.
- [ ] Messaging: Domain events inherit from `Entity<T>` and respect provider capabilities.

---

## Next Steps

- Validate new recipes against the stage checklists in the [Data Modeling Playbook](../guides/data-modeling.md#8-validate-the-aggregate).
- Wire live APIs using the [API Delivery Playbook](../guides/building-apis.md).
- Instrument semantic pipelines with the [Koan Troubleshooting Hub](../support/troubleshooting.md).

---

## Validation

- Last reviewed: 2025-09-28
- Verified against Koan v0.6.2 sample services and Flow adapters.
