---
type: GUIDE
domain: observability
title: "Inspect Koan Runtime Facts"
audience: [developers, operators, maintainers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: verified
  scope: schema 2 decisions, guarantees, corrections, startup, HTTP, and MCP projections
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

Schema 2 adds the explicit `guarantee` kind. A guarantee is a value-free statement compiled from a
concern-owned plan or realization receipt. Startup selects guarantees by this kind—not by a pillar's
fact code—while Web and MCP continue to project the complete envelope.

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

`guarantee` is an explanation category, not a health verdict. Read its stable capability tokens and
reason alongside the named provider/realization bounds; do not infer confidentiality, topology,
durability, or exactly-once behavior from an unrelated guarantee.

## Authoring rule

Do not create ad hoc fact DTOs or copy provider payloads into this envelope. Add a typed contribution
at the decision owner, reuse that decision for behavior and reporting, and provide a stable reason
code plus safe correction. If the proposed shared field is meaningful only to one provider, keep it
with that provider.

## Current boundary

The proved schema covers module activation, provider elections, lockfile status, hard-segmentation
realizations, Communication and Jobs context guarantees, bounded relationship decisions, and reporter
failures. It remains a current host explanation—not telemetry history, a provider-fleet certification,
or proof that every discovered capability is healthy.

## References

- [ARCH-0111](../decisions/ARCH-0111-unified-runtime-facts.md)
- [ARCH-0112](../decisions/ARCH-0112-bounded-relationship-negotiation.md)
- [Product constitution](../architecture/product-constitution.md)
- [Capability evidence ledger](../initiatives/koan-v1/CAPABILITIES.md)
