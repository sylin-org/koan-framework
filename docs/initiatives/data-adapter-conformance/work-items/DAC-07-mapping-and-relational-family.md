---
type: SPEC
domain: data
title: "DAC-07 Build the Mapping Compiler and Relational Family Substrate"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: passed
  scope: mapping compiler and shared relational execution substrate
---

# DAC-07 — Build the mapping compiler and relational family substrate

| Field | Value |
|---|---|
| Phase / kind | foundation / remediation |
| Depends on | DAC-06 |
| Unlocks | DAC-08 |
| Primer IDs | A-07, E-01–E-15, P-02–P-04, P-06 |
| Production writes | only mapping contracts under `src/Koan.Data.Abstractions/**` and `src/Koan.Data.Core/**`, `src/Koan.Data.Relational.Abstractions/**`, `src/Koan.Data.Relational/**`, `tests/Suites/Data/Relational/**`, affected shared tests, and initiative evidence; no connector paths |
| Owner | Framework mapping plus Relational Family mechanics |

## Meaningful outcome

One compiled logical-property-to-physical-binding plan drives hydration, writes, queries, patches, conditional writes,
projections, indexes, and validation across identity-plus-object, flat-name, hybrid, and nested-path shapes.

## Application contract

**Application intent:** “Use my ordinary Entity model with this physical record shape, and make every operation agree
about that decision.”

```csharp
koan.Data.Source("LegacyErp").Map<Customer>(map => map
    .Container("dbo", "CUSTOMER")
    .Key(customer => customer.Id).Name("CUSTOMER_NO")
    .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
    .Property(customer => customer.Profile).Object("PROFILE_JSON"));
```

**Guarantee:** One immutable host-owned plan compiles the complete identity, logical authority, physical location,
direction, generation, codec, and CLR-access decisions. Every hydration/write/filter/order/patch/conditional/
projection/index consumer resolves that same binding and encoding; invalid or incomplete maps reject before provider
dispatch. External lifecycle never mutates shape, and optimized projection/index/TTL claims require native proof.

**Complete surface:** Configure a source, declare one map per source/entity in `AddKoan`, and use ordinary Entity
operations. Providers consume the compiled map through a narrow plan service and lower only physical paths, values,
commands, and definitions. Relationships, change tracking, unit of work, implicit joins, schema migration, and
universal LINQ remain outside the surface.

**Coalescence and ergonomics:** Reuse source composition ownership, `StorageAddress`, strict logical `FieldPath`
resolution, source lifecycle policy, and inert relational dialect/DDL seams. Replace shallow projection metadata as
the new Family's semantic owner; retain only a plan-delegating compatibility wrapper for adapters awaiting their own
cards. The fluent chain states source/container once and exposes only the next distinct decision (`Key`/`Property`,
then `Name`/`Path`/`Object`, then optional behavior).

## Required work

1. Replace shallow projection metadata as the semantic owner with the ratified immutable mapping compiler.
2. Implement the ratified `Container`/`Key`/`Property`/`Name`/`Path`/`Object` grammar over one neutral descriptor.
   Support authoritative writable/read-only/generated bindings, nested logical and physical paths, scalar/structured
   values, symmetric codecs, whole-object/flat/hybrid shapes, and single/composite/generated identity.
3. Reject duplicate authority, ambiguous paths, partial identity, incompatible types, undeclared asymmetry, and missing
   required insert bindings before business dispatch.
4. Make every mapping consumer use the same plan and receipt. Selective projection/index claims require native proof.
5. Turn `Koan.Data.Relational` into a real family execution substrate for common command planning, parameter binding,
   materialization, mapping consumption, schema validation, and lifecycle orchestration. Keep dialect, SDK, native type,
   connection mode, DDL syntax, and exact failure codes in adapters. Implement the Family contract directly; do not
   extract, move, copy, port, or mechanically transform current SQLite/provider implementation into it.
6. Preserve external lifecycle: mapping validates but cannot create/alter shape under External.
7. Keep relationships, unit of work, implicit joins, and universal LINQ outside this work.

## Evidence anchors

- `src/Koan.Data.Core/ProjectionResolver.cs`
- `src/Koan.Data.Core/ProjectedProperty.cs`
- `src/Koan.Data.Relational.Abstractions/Orchestration/**`
- `src/Koan.Data.Relational/**`
- DAC-01/DAC-02 contract rows, authoritative relational provider documentation/probes, and provider-neutral black-box
  fixtures; no gold or sibling connector source, tests, history, or internal design is an input to this card
- `tests/Suites/Data/Relational`

## Verification

- Compile-plan oracle covers all four mapping shapes, codecs, composite/generated identities, missing/null complex
  values, every consumer, conflict failures, host isolation, cache bounds, and warm reuse.
- Native-spy/dialect tests prove the Family emits a complete plan and adapters only lower/execute it.
- Mutation checks bypass the plan in one write/filter/index consumer and must fail E-11.
- External-policy tests prove zero shape mutation.

## Definition of done

- [x] Mapping and Relational Family rows are green without requiring SQLite-specific policy code.
- [x] P-06 shows one owner for mapping, materialization, schema orchestration, and native dialect lowering.
- [x] Gold adapter cards can be lean provider implementations rather than framework copies.
- [x] No legacy gold/provider implementation lineage was moved into the Relational Family.

## Stop conditions

Stop if moving behavior would force document/KV providers to adopt relational concepts or if a shape requires ORM
relationship semantics outside the primer boundary.
