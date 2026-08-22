---
id: ARCH-0132
slug: a-registry-separates-declaration-from-inference
domain: Architecture
status: Accepted
date: 2026-08-22
title: A registry separates a declaration from an inference
related:
  - ARCH-0126
  - ARCH-0086
  - DATA-0122
---

# ARCH-0132: A registry separates a declaration from an inference

## Context

The SQLite connector suite reported a cluster of failures four times across two days — four to nine specs,
once 48 of 49 — and passed on immediate re-run every time. Three investigations concluded "environment",
eliminating test parallelism, fixture paths, connection pooling and fault containment. All four eliminations
were sound and all four were beside the point.

The cause was in `Koan.Core`. `ProvenanceRegistry.ResolveDescriptor` invents a placeholder descriptor for any
pillar no manifest has declared, so a module reporting under an unknown pillar code still has something to be
shown under — and it **registered that guess in `KoanPillarCatalog` as though it were a declaration**. When
`DataPillarManifest` subsequently declared the real `data` pillar, the colour and icon differed, the catalog
refused the registration as a conflict, `KoanDataCoreModule` threw during register, and every `AddKoan()` in
the process failed from that point on. `DataPillarManifest` only latches after a successful registration, so
each later boot retried and threw again: one poisoning, then a cascade.

Whether it happened at all depended on which module reported provenance before `Koan.Data.Core` registered —
an ordering question. That is why it presented as flakiness, why the cluster size varied, and why it never
reproduced in isolation.

The catalog's refusal was correct. Writing a guess into it was not.

## Decision

**Process-global registries hold declarations. A value invented to fill a gap is returned to its caller and
never registered.**

- `ProvenanceRegistry` describes an undeclared pillar without declaring it. Nothing reads the catalog for
  pillars nobody declared: `KoanPillarCatalog.All` has no readers, the admin status page matches by namespace
  prefix which a guess never populates, and `ResolveDescriptor` runs once per pillar code because its caller
  caches the result. The write achieved nothing except the collision.
- `KoanPillarCatalog.RegisterDescriptor` therefore has one kind of caller — a manifest stating what it owns —
  and its refusal of two differing descriptions is reachable only from declarations, where a disagreement is a
  genuine contradiction.

**When adding process-global state, ask which derivations can reach a key.** One is fine. Two needs an explicit
rule about which wins, and a guess never outranks the module that owns the thing.

A sweep of every static registry in the tree found this shape exactly once more, and the taxonomy is worth
keeping:

| Shape | Examples | Why it is safe |
|---|---|---|
| Value derived from the key | `ProviderMetadata`, `EntityTypeCatalog` | Two writers cannot disagree |
| Union of contributions | `ManagedFieldRegistry`, naming and write-contributor registries | A late writer can only add |
| Single writer | `KoanRegistry`, `DatabaseRouteRegistry` | Nothing to reconcile |
| **Two derivations of one key** | `KoanPillarCatalog` (fixed); `KoanRegistry.RegisterSemanticModule` | One is authoritative and one is not — the rule must be explicit |

The second instance is latent rather than live: the source generator derives a module's identity from package
identity and `RegistryManifestLoader` derives it from assembly metadata with a fallback to the assembly name.
They agree by construction today. The guard that would catch a disagreement was being discarded by a `catch`
wider than its own comment, which now catches only the reflection-load exceptions it documents.

## Consequences

- A boot cannot be poisoned by a description nobody declared, and the catalog keeps refusing genuine
  contradictions.
- The rule is a design question to ask of new registries rather than a mechanism enforcing itself. That is a
  real limit: nothing prevents a future writer from registering a guess, and the protection is the taxonomy
  above plus review.
- A related discipline, earned three times this cycle: **"intermittent, did not reproduce" means "was not
  captured".** PMC-042 was a tie-order defect, this was boot-order poisoning, PMC-048 was a SQL Server
  deadlock. None reproduced in isolation; all three reproduced under the batch shape that first produced them
  and named themselves in one line once the output was preserved rather than read through a summary filter.

## References

- `src/Koan.Core/Modules/Pillars/KoanPillarCatalog.cs`
- `src/Koan.Core/Provenance/ProvenanceRegistry.cs` — `ResolveDescriptor`
- `src/Koan.Core/Hosting/Registry/RegistryManifestLoader.cs` — the narrowed catch
- `tests/Suites/Core/Koan.Core.Tests/PillarCatalogSpec.cs`
- `docs/initiatives/koan-v1/POST-CYCLE-TODO.md` — PMC-053
