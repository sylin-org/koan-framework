---
type: SPEC
domain: framework
title: "R14 - Make Public Documentation One Greenfield Product Surface"
audience: [architects, maintainers, developers, ai-agents]
status: passed
last_updated: 2026-07-22
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-22
  status: passed
  scope: public documentation graph, canonical ownership, product truth, links, and agent retrieval
---

# R14 — Make public documentation one greenfield product surface

- Tranche: `T9 — public product language`
- Status: `passed`
- Depends on: completed R12/R13 product surface and the accepted documentation posture
- Owner: the public capability curriculum, not individual historical document locations

## Meaningful outcome

A developer or coding agent can begin with a business need, find one canonical Koan capability path,
write the smallest current application expression, understand its guarantee and corrective failure,
and reach the appropriate package, provider, sample, and operational detail without encountering a
competing tutorial, historical plan, or duplicate contract.

Public documentation reads as if the application is being built today. Historical evolution remains
available in ADRs, initiatives, assessments, and archives but does not leak into current guidance.

## Audiences

- **Application developers** need one greenfield path from intent to working code.
- **Coding agents** need predictable headings, exact package and type names, explicit prerequisites,
  guarantees, corrections, and canonical-source precedence.
- **Operators and support engineers** need provider selection, health, facts, diagnostics, limits, and
  safe corrective actions.
- **Package authors and maintainers** need package boundaries, extension contracts, lifecycle, and
  compatibility detail without creating a second application-facing path.
- **Architects** need current product laws separated from dated decision history and implementation
  plans.

The same canonical pages serve humans and agents. `llms.txt` is a retrieval map, not a duplicated
manual.

## Public documentation boundary

The existing `scripts/public-docs-lint.ps1` graph is the inventory owner. At opening it reports:

- 319 current Markdown or text surfaces;
- 193 NuGet-delivered package companions;
- 18 current documents admitted by status rather than public navigation;
- 120 historical Markdown boundaries;
- 42 navigation links with 34 unique targets;
- 11 graduated sample roots.

R14 changes the quality and ownership of that graph. It does not add a second permanent inventory or
make ADRs current usage documentation.

## Documentation ownership

| Information | Canonical owner |
|---|---|
| Product promise and first meaningful result | root `README.md` |
| Learning order and navigation | `docs/index.md` and `docs/toc.yml` |
| Application semantics, guarantee, and correction | capability pillar page |
| Provider/package-specific setup and limits | package `README.md` |
| Internals, lifecycle, and extension contracts | package `TECHNICAL.md` |
| Support maturity and package identity | generated product surface |
| Complete business journey | graduated sample |
| Agent retrieval | `llms.txt` and current agent instructions |
| Historical reasoning | ADRs, initiatives, assessments, and archive |

Pages link to their owner instead of reproducing its contract. Package documentation may state the
provider-specific delta but may not redefine pillar semantics or support maturity.

## Capability curriculum

The public curriculum is organized by application capability rather than document type or assembly
layout:

1. Start;
2. Foundation and composition;
3. Data;
4. Web;
5. Identity and isolation;
6. Work and communication;
7. State and content;
8. Intelligence;
9. Agents;
10. Canon;
11. Testing and operations.

Each pillar uses one contract: business need, when to use it, installation, smallest public
expression, guarantee, configuration/provider selection, inspection, corrective failure, limits,
and deeper package/sample/API links.

## Execution

### A. Establish the surface and front doors

- Classify every current public document as canonical, package delta, sample, redirect, or historical.
- Align the root README, docs home, TOC, `llms.txt`, template guide, and samples index around one
  greenfield path.
- Remove current-status admission from unlinked plans or research.

### B. Prove the contract through Data

- Make Data the first complete vertical because it spans Entity semantics, provider choice,
  configuration, corrective failure, package companions, diagnostics, and samples.
- Merge the current Data card, reference, modeling, access, streaming, and provider guidance into one
  coherent pillar with subordinate task pages only where they own distinct actions.
- Reduce Data package READMEs to package-specific value, setup, limits, and links; keep internals in
  technical companions.

### C. Migrate the remaining pillars

- Foundation and Web;
- Identity and isolation;
- Work, communication, state, and content;
- AI, vector, search, Agents, and Canon;
- Testing, operations, support, samples, and package companions.

Each slice deletes or reclassifies the paths it supersedes before moving to the next pillar.

### D. Validate the complete public product

- Run public-doc graph, link, frontmatter, retired-language, generated-output, and code-example checks.
- Prove representative clean-room tasks from public documentation alone.
- Confirm every supported product claim has one canonical capability home and no public page competes
  with generated maturity truth.

## Greenfield language contract

Current public pages:

- begin with the application intent and observable result;
- use present-tense 0.20 syntax, package IDs, configuration, health, and facts;
- name guarantees, provider limits, non-claims, and corrective failures;
- avoid initiative history, implementation chronology, migration language, and repository mechanics;
- contain no private downstream identity or recognizable detail;
- use one canonical application front door and link to deeper owners.

Version history appears only in an explicit migration page. ADRs remain dated evidence and are not
edited by this epic.

## Acceptance

R14 passes when:

1. every supported product claim resolves to exactly one canonical pillar home;
2. every current public page is reachable from navigation or an owning package/sample index;
3. every unlinked present-tense plan, proposal, or duplicate is merged, archived, or removed;
4. public examples use current package IDs and the canonical `AddKoan()` / Entity grammar;
5. package READMEs own only package-specific application value while technical companions own
   internals and extension detail;
6. generated product truth remains the sole maturity authority;
7. public graph, links, frontmatter, generated-output, privacy, and canonical-example gates pass;
8. clean-room developer and coding-agent tasks succeed without source-code discovery;
9. no new documentation ledger, parallel manual, or ADR rewrite is introduced.

## Completion evidence

- All 40 promoted product claims resolve to exactly one canonical capability home; none points to an
  ADR, initiative, assessment, or archive as current guidance.
- Every current public document is reachable. The final graph contains 689 current assets, 668
  current text surfaces, 132 historical boundaries, 51 navigation targets, and 11 graduated sample
  roots, with zero current-document orphans.
- The root README, documentation home, quickstart, table of contents, guide index, and `llms.txt`
  teach one capability-led greenfield path.
- Data and the remaining capability pillars have canonical owners. All 15 legacy capability cards
  are concise superseded pointers rather than competing manuals.
- Twenty agent skills resolve cleanly to the canonical surface. The strict skills gate reports zero
  errors and zero warnings.
- Twenty-two package-local maturity/publication statements were removed. A permanent lint rule keeps
  generated product truth as the sole maturity authority.
- S3 and Backup remain explicitly shelved and are not presented as greenfield capabilities.
- Public documentation truth, focused link/frontmatter checks, generated product-surface currency,
  privacy inspection, and repository whitespace checks pass.
- ADRs were not changed. No private downstream identity, artifact, path, or distinctive workflow was
  introduced.

This epic intentionally did not require DocFX/API generation, dependency restoration, or a broad
framework build. Those are separate engineering validations and are not evidence for the quality of
the public project documentation surface.

## Boundaries

- Do not edit ADRs.
- Do not turn historical material into present-tense public guidance.
- Do not create separate human and agent manuals.
- Do not preserve duplicate prose for URL stability; use a concise redirect only where an external
  route genuinely matters.
- Do not claim support beyond the generated product surface.
- Do not disclose private downstream identity, artifacts, paths, or distinctive workflows.
- Keep the work local and uncommitted until the maintainer explicitly requests publication.
