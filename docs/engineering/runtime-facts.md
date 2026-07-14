---
type: GUIDE
domain: observability
title: "Inspect Koan Runtime Facts"
audience: [developers, operators, maintainers, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: verified
  scope: module activation, default data-adapter election, and relationship negotiation vertical slices
---

# Inspect Koan runtime facts

## Contract

- **Input**: the current host's completed or in-progress runtime composition.
- **Output**: schema-versioned, redacted, deterministically ordered facts with stable codes and
  corrective guidance.
- **Failure behavior**: collection begins as incomplete; reporter failures become explicit
  `collectionFailed` facts and degraded health.
- **Security**: no arbitrary payload, configuration value, raw exception message, or stack trace is
  part of the fact schema.

## Read facts in-process

Resolve `IKoanRuntimeFacts` from the active host:

```csharp
var runtimeFacts = services.GetRequiredService<IKoanRuntimeFacts>();
var snapshot = runtimeFacts.Current;

if (!snapshot.Complete)
{
    // Collection has not produced a verdict yet.
}

foreach (var fact in snapshot.Facts)
{
    Console.WriteLine($"{fact.Code}: {fact.Summary}");
}
```

Treat `Code`, `ReasonCode`, `Kind`, and `State` as machine fields. Treat `Summary` and `Correction` as
human guidance whose exact wording may improve without a schema change.

## Read facts over Web or MCP

- Web: `GET /.well-known/Koan/facts`
- MCP: read resource `koan://facts`

The two surfaces use `KoanFactJson` and return the same envelope. Web exposure is available in
Development. Outside Development, explicitly set:

```json
{
  "Koan": {
    "Web": {
      "ExposeObservabilitySnapshot": true
    }
  }
}
```

Apply the same access controls used for operational diagnostics. Redaction prevents secrets from
entering the contract; it does not make topology names public information.

## Interpret collection state

| Envelope/fact state | Meaning |
| --- | --- |
| `complete: false` | Koan has not completed collection; do not infer health. |
| `selected` / `observed` / `healthy` | The named decision or observation completed. |
| `unknown` | Koan lacks enough evidence for a verdict. |
| `degraded` | The application continued with a visible limitation. |
| `rejected` | Koan refused a module or decision. |
| `collectionFailed` | A reporter failed; its missing facts are not treated as success. |

Health impact follows `Kind`, not `State` alone. A request-level capability decision may be
`rejected` because Koan safely refused an unbounded operation while the host remains healthy.
`Rejection`/`Degradation` kinds and collection failures affect the runtime-facts health contributor.

## Authoring rule

Do not create ad hoc fact DTOs or copy provider payloads into this envelope. Add a typed contribution
at the decision owner, reuse that decision for behavior and reporting, and provide a stable reason
code plus safe correction. If the proposed shared field is meaningful only to one provider, keep it
with that provider.

## Current boundary

The proved schema currently covers module activation, composition reporter failure, lockfile status,
default data-adapter election, and child relationship execution/rejection. Relationship proof covers
InMemory, JSON, and SQLite direct child edges; it is not a provider-fleet, recursive-graph, or request
audit-history claim.

## References

- [ARCH-0111](../decisions/ARCH-0111-unified-runtime-facts.md)
- [ARCH-0112](../decisions/ARCH-0112-bounded-relationship-negotiation.md)
- [Product constitution](../architecture/product-constitution.md)
- [Capability evidence ledger](../initiatives/koan-v1/CAPABILITIES.md)
