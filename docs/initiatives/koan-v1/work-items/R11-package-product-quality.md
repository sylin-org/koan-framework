---
type: SPEC
domain: framework
title: "R11 - Graduate the NuGet Product Surface"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: package-quality contract and deterministic assessment owner
---

# R11 — Graduate the NuGet product surface

- Tranche: `T7B — V1 release readiness / package-product graduation`
- Status: `in-progress`
- Depends on: passed R09 and R10; prepared R08-05
- Guards: exact R08-05 candidate and initial coherent public observation
- Owner: public package identity, reference intent, package-specific documentation, consumer proof, and package presentation

## Mandate

A NuGet package is a product promise, not a build artifact with metadata. Every package that remains public must
represent a distinct reference intent, explain the meaningful capability that appears, and prove its smallest honest
consumer result. A package that cannot earn a distinct intent is merged, renamed, split, or retired before V1.

R11 does not award subjective percentages. A package is graduated only when every applicable contract is proved;
machine-checkable structure, architectural disposition, prose judgment, and executable evidence remain visibly
distinct. Package availability never implies maturity or support.

## Application intent

> A developer or coding agent can find any Koan package, understand why to reference it, reach its smallest meaningful
> result, inspect what was composed, and understand its defaults, guarantees, corrections, and limits without reading
> framework internals.

The maintainer-facing assessment expression is one read-only command:

```powershell
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md
```

It evaluates the same MSBuild project graph that owns release identity. It adds no package-author attribute, role
registry, maintained package list, release mutation, or operator configuration. Invalid facts fail with the owning
package and a correction. The first report names structural readiness and review signals; it does not promote a
package to graduated by inference.

## Package-product laws

1. **Existence is earned.** A package owns a distinct reference intent, contract boundary, provider choice, projection,
   tool, analyzer, template, or independently useful artifact.
2. **Standard facts are authority.** Role and shape derive from evaluated MSBuild/NuGet facts, dependency shape,
   ordinary project structure, and the public package name. Ambiguity is a boundary defect, not a reason for new metadata.
3. **Reference states intent.** Adding a functional package either contributes its named capability or fails with a
   correction. Contract-only packages remain inert.
4. **Every package explains itself.** No public package may inherit the framework root README as its NuGet page.
5. **Documentation is proportional.** Every package owns orientation and first-use truth. Separate technical
   documentation is required when runtime activation, election, lifecycle, build behavior, or non-obvious ownership
   needs a deeper contract; pure vocabulary does not earn ceremonial duplication.
6. **Metadata is semantic.** Descriptions and tags describe this package, not the whole framework. Shared metadata
   carries identity and provenance only where the fact is universally true.
7. **Bytes are inspectable.** The canonical icon, README, license, repository commit, dependency ranges, symbols,
   SourceLink, and intended package contents are verified from the nupkg/snupkg.
8. **The shortest result is real.** Entry packages and runtime capabilities restore into a clean consumer and prove
   the smallest meaningful business or module-author result they advertise.
9. **Operators and agents see the same decision.** Defaults, activation, facts, health, errors, and unsupported
   guarantees agree across package docs and runtime projections.
10. **Greenfield means one current path.** Rename, merge, split, and retirement decisions happen before public
    observation; R11 introduces no compatibility aliases for accidental pre-V1 shapes.

## Derived package roles

The quality compiler derives presentation roles without a new package property:

- entry bundle or template;
- runtime foundation or capability;
- contracts or abstractions;
- adapter, connector, or provider;
- projection or integration;
- analyzer, generator, tool, or content/build asset.

If a role is not legible from standard facts and public identity, R11 reviews the package boundary. A derived role is
presentation evidence, not a second runtime activation mechanism.

## Graduation contract

Every surviving package must satisfy the applicable cells:

1. explicit keep/merge/split/rename/retire disposition;
2. clear name, responsibility owner, and dependency boundary;
3. outcome-oriented description and truthful package-specific tags;
4. canonical embedded icon and package-owned README;
5. install/reference instruction and the smallest meaningful result;
6. reference effect, defaults, configuration, inspection, corrective failures, and limitations;
7. technical ownership for runtime/build behavior where applicable;
8. exact nupkg dependency/content/provenance/symbol proof;
9. clean-consumer compile or execution proportional to the package role;
10. product-surface and support claims that agree with executable evidence.

`structurally-ready` in the generated report means only that objective checks found no repair. It is not a support or
graduation claim. Architectural and prose review plus the applicable executable proof remain required.

## Execution order

| Child | Outcome | Status |
|---|---|---|
| [R11-01](r11/R11-01-quality-contract-and-compiler.md) | one deterministic read-only assessment owner and generated baseline | passed |
| [R11-02](r11/R11-02-package-topology-inventory.md) | exact package disposition queue before mass documentation work | in-progress |
| [R11-03](r11/R11-03-package-identity-substrate.md) | canonical mascot, truthful shared metadata, owned-README and packed-content policy | passed |
| [R11-04](r11/R11-04-golden-package-journey.md) | golden package journey behind AddKoan + Entity + EntityController | passed |
| R11-05 | dependency-ordered family graduation with coalescence before polish | pending |
| R11-06 | NuGet rendering, clean-consumer, operator, reviewer, and agent proof | pending |
| R11-07 | complete active-package disposition and one final release-certification boundary | pending |

Open later child cards only when the preceding slice establishes the facts needed to size them. This keeps R11 a
sequence of meaningful decisions rather than 108 parallel documentation chores.

## Validation economy

- During a package-family slice, run the quality compiler, pack only affected owners, and execute the direct consumer
  and focused test cells.
- Do not rerun the complete release ratchet for README, metadata, or family-local repairs.
- Run the full package graph, templates, FirstUse, GoldenJourney, and public documentation gates once at the R11-07
  boundary before creating the exact R08-05 candidate.

## Acceptance

R11 passes only when:

1. every active package has a terminal architectural disposition and every survivor earns a distinct reference intent;
2. no package uses the root README as its package page;
3. descriptions, tags, icon, docs, dependency graph, package contents, and provenance pass their objective gates;
4. every package has proportional human review and consumer evidence for its role;
5. generated package quality, product surface, public docs, and current source agree;
6. experimental packages state their unsupported boundaries and cannot be mistaken for supported foundation;
7. the final focused family proofs and one complete boundary certification pass without public mutation.

## Stop conditions

- Stop package prose work when the package boundary or name is still under review.
- Stop if a quality rule creates a project attribute, duplicate package list, or manual fact already available from MSBuild.
- Stop if a mechanical heuristic is presented as proof of prose quality or support maturity.
- Stop if polishing a package would preserve a duplicated responsibility that should be coalesced.
- Stop before R08-05 publication until R11 passes and separate remote-operation authorization is renewed.
