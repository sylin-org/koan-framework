# Composition diagnosis

Use this reference when an application fails to start, selects the wrong provider, behaves incorrectly, reports unhealthy, performs poorly, or exposes inconsistent HTTP/MCP behavior.

## Read the evidence in ownership order

1. Reproduce the failing user or operator journey and capture the exact error.
2. Inspect references, `AddKoan()`, configuration intent, facts, lock evidence, and health.
3. Find the owning capability's current public contract and focused tests.
4. Separate what is observed from what is inferred.

Classify the boundary:

- **Availability:** is the owning capability referenced?
- **Selection:** which provider or route won?
- **Configuration:** did its declared intent resolve?
- **Capability:** can the selected provider perform this operation?
- **Behavior:** did application policy produce the result?

Do not add registration calls until evidence shows a missing application-owned seam. References and one `AddKoan()` normally own composition.

## Correct the smallest owner

Form one falsifiable cause, change the smallest owning layer, rerun the failing journey, and confirm composition did not silently change. Then exercise the invalid or missing dependency path and improve its correction if it remains opaque.

Common corrections:

- Make an important adapter/source route explicit when the wrong provider wins.
- Preserve the first owning startup error instead of masking it with fallback.
- Keep a required unhealthy dependency unready; optional degradation must be explicit policy.
- Replace unbounded queries with pages or provider-backed streams without changing correctness.
- Route HTTP and MCP through the same application operation, authorization, tenant, and Entity constraints.
- Assert provider participation when tests pass against the wrong substrate.

Lead the handoff with the root cause, repaired outcome, evidence that ruled alternatives out, proof run, and any boundary that could not be exercised.
