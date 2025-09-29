---
id: ARCH-0056
slug: koan-canon-pillar-renaming
domain: Architecture
status: approved
date: 2025-09-29
title: Koan.Canon Pillar Renaming
supersedes:
  - ARCH-0053
---

## Contract

- **Inputs**: Koan.Flow pillar modules, associated documentation, NuGet packages, samples, and automation scripts slated for renaming.
- **Outputs**: Unified Koan.Canon pillar naming across source, packaging, documentation, and operational tooling, plus migration guidance for dependents.
- **Error Modes**: Partial rename leaving mixed namespaces, stale package IDs breaking restores, unattended queue/topic identifiers, and documentation drift.
- **Success Criteria**: Source and package identifiers consistently emit `Koan.Canon`, solution builds and docs pipelines succeed, and pillar documentation reflects the new canonicalization terminology.

## Context

Koan.Flow originated as Koan's canonicalization engine but the "Flow" name led to recurring confusion with generic ETL pipelines. The pillar now encompasses identity linking, aggregation, projection, and lineage features that canonize heterogeneous inputs into reference entities. Because the framework remains greenfield, we can execute a breaking rename without migration baggage.

Prior work in [ARCH-0053](./ARCH-0053-Koan-flow-pillar-entity-first-and-auto-registrar.md) documented the Flow pillar's goals but retained the ambiguous branding. To align terminology with actual scope and unblock future documentation and package clarity, the pillar should be renamed to **Koan.Canon**.

## Decision

**Approved**: Replace the Koan.Flow pillar naming with **Koan.Canon** across all source projects, namespaces, packages, documentation, and samples. Treat the rename as a single, cohesive refactor executed on `dev`.

### Rename Directives

- Project folders and `.csproj` files adopt `Koan.Canon.*` naming. `<PackageId>`, `<AssemblyName>`, and `<RootNamespace>` mirror the new brand.
- Namespaces, type prefixes, and helper classes update from `Koan.Flow` to `Koan.Canon`. Canonization-specific types (e.g., `FlowEntity`) may receive clearer names (`CanonEntity`) while preserving semantics.
- Queue, telemetry, and configuration identifiers using the `Koan.Flow` prefix migrate to `Koan.Canon` equivalents.
- Documentation, ADRs, and TOCs reference the pillar as Koan.Canon, noting superseded Flow materials.

## Options Considered

| Option | Outcome | Evaluation |
| --- | --- | --- |
| Keep "Koan.Flow" and add clarifying docs | Minimal engineering work | Rejected. Branding ambiguity persists and contradicts the pillar's canonization charter. |
| Rename to "Koan.CanonicalFlow" | Hybrid name | Rejected. Retains the confusing "Flow" noun while creating verbose identifiers. |
| Rename to "Koan.Reference" | Focus on reference entities | Rejected. Understates ingestion and projection responsibilities; risks conflation with read-only stores. |
| **Rename to "Koan.Canon"** | Canonization-focused naming | **Accepted.** Concise, signals purpose, aligns with `ReferenceItem`, `CanonicalProjection`, and identity linkage vocabulary. |

## Implementation Guidelines

1. **Plan the sweep**: list every Flow project, package, sample, test assembly, and documentation page. Freeze other work until refactor completes.
2. **Rename structure**: use scripted moves (PowerShell or `git mv`) for `src/Koan.Flow.*`, matching `.csproj` names and solution entries.
3. **Namespace pass**: replace `Koan.Flow` with `Koan.Canon` in source, adjust type names where clarity improves, and regenerate queue/topic constants.
4. **Packaging updates**: revise package metadata, release scripts, and any CLI verbs referencing Flow.
5. **Docs refresh**: migrate pillar references (`docs/reference/flow/**`), update TOCs, and document the rename in changelog notes.
6. **Validation**: run `dotnet build`, targeted tests, `scripts/build-docs.ps1 -Strict`, and code example validation to catch residual references.
7. **Communication**: publish release notes explaining the rename and pointing to the new Koan.Canon documentation entry.

## Consequences

- **Positive**: Eliminates branding ambiguity, clarifies the pillar's responsibility, and simplifies future documentation and marketing.
- **Tradeoffs**: Large refactor touching multiple projects and samples; risk of transient broken references during execution.
- **Neutral**: Canonicalization workflows and APIs remain conceptually identical aside from naming.

## Follow-ups

- Update `docs/reference/flow/index.md` content to the Koan.Canon nomenclature while relocating the file.
- Regenerate pillar-specific templates and scaffolding references.
- Notify module owners for dependent pillars (Messaging, Orchestration, Samples) to synchronize terminology.

## References

- [ARCH-0053 – Flow pillar entity-first and auto-registrar](./ARCH-0053-Koan-flow-pillar-entity-first-and-auto-registrar.md)
- [DATA-0070 – External ID correlation framework](./DATA-0070-external-id-correlation.md)
- Internal analysis on Koan.Flow responsibilities (2025-09-28)
