---
type: SPEC
domain: framework
title: "R10-08 - Rebuild the public documentation as one current product surface"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: public information architecture, front doors, current guides, reference cards, samples, package companions, and documentation gates
---

# R10-08 — Rebuild the public documentation as one current product surface

- Tranche: `T7B — public-surface truth before further sample graduation`
- Status: `passed`
- Depends on: R09 semantic composition kernel; R10-01 through R10-06 graduated samples
- Preserves: every file under `docs/decisions/` unchanged

## Task

Make Koan's public documentation read as one greenfield product. A developer or coding agent must not
encounter a removed bootstrap mechanism, retired package family, migration plan, synthetic sample claim,
or historical version presented as a supported alternative.

## Application intent

“From a new checkout, learn the smallest supported Koan application, achieve one meaningful result,
add capabilities through current Entity-first semantics, and inspect exactly what the framework chose.”

## Public expression

The front door teaches one host, one model grammar, and one API expression:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

```csharp
public sealed class Todo : Entity<Todo>;
public sealed class TodosController : EntityController<Todo>;
```

Package/project references express available capability. `AddKoan()` compiles referenced modules once.
Runtime reports, health, HTTP facts, and MCP facts project the resulting decisions. The generated product
surface is the maturity authority; package existence alone is never a support claim.

## Guarantee and correction

- `README.md`, `docs/index.md`, `llms.txt`, getting-started pages, public navigation, the sample index,
  and current pillar cards agree on present APIs, routes, package status, and maturity.
- Public navigation contains product guidance only. Initiatives, assessments, implementation plans,
  proposals, and archives remain repository evidence but are not alternate curriculum.
- Removed identifiers and packages fail a public-doc gate. Current examples use awaited host lifetime,
  canonical health routes, and current module/Communication terminology.
- An unassessed package remains visibly unassessed. A not-yet-published coherent package wave is not
  described as installable merely because local package evidence exists.
- ADR files are immutable during this pass. They remain the dated decision record and may describe the
  architecture at the time of each decision.

## Exploration evidence

- The existing TOC mixed current guidance with migration plans, initiatives, archived implementation
  documents, proposals, a retired Messaging reference, and stale case studies.
- Current public-looking documents still named deleted `KoanAutoRegistrar`, `IKoanAutoRegistrar`,
  `IKoanInitializer`, and `Koan.Messaging.*` surfaces.
- Front doors advertised S14 as a provider benchmark even though R10-07 has assessed that application for
  break-and-rebuild and rejected its ranking claims.
- Several pages called `/api/health` or `/health` canonical instead of `/health/live` and `/health/ready`,
  used non-awaited `app.Run()`, or described the framework as one fixed `0.17.x` package version despite
  independent package version ownership.
- `docs-lint.ps1` passed structural checks with zero errors, demonstrating that link/frontmatter lint alone
  cannot protect semantic product truth.

## Coalescence

- `docs/toc.yml` is the single public curriculum manifest.
- `docs/reference/product-surface.md` is the generated package/maturity authority.
- `samples/README.md` is the only active sample portfolio authority.
- the product constitution, Entity semantics contract, and principles are the architecture front door.
- one focused public-doc gate protects retired symbols, historical routes, public navigation boundaries,
  and canonical host lifetime instead of repeating manual review rules across documents.

## Focused acceptance

1. The repository front door reaches a meaningful FirstUse result without teaching an unavailable package path.
2. The public TOC contains no archive, initiative, assessment, proposal, migration-plan, or retired Messaging page.
3. Every TOC-linked page is scanned for retired bootstrap/Messaging vocabulary and stale S14/version/health claims.
4. Root README, docs home, quickstart, overview, `llms.txt`, sample index, Core/Data/MCP/Jobs references,
   and architecture principles agree with current source and generated maturity evidence.
5. Package and module companion documents that describe removed activation machinery are corrected or explicitly
   historical; generated product-surface text is regenerated from its source.
6. Changed validated C# examples compile, docs links pass, the public truth gate passes, the diff is clean, and
   `docs/decisions/` has no changes.

## Acceptance evidence

- Public navigation is now 36 product-only targets. Initiatives, assessments, plans, proposals,
  archives, the retired Messaging reference, stale case studies, and ungraduated samples are absent.
- Root README, docs home, quickstart, golden path, `llms.txt`, `CLAUDE.md`, samples index, architecture
  front door, Core/Data/Web/Communication/Jobs/MCP cards, health wording, and package status agree.
- `scripts/public-docs-lint.ps1` scans 174 current files: every TOC-linked page, root/agent front door,
  sample index, and package-owned README/TECHNICAL companion. It rejects retired bootstrap and Messaging
  terms, stale sample/version/health claims, non-awaited web hosts, unprefixed or fixed package install
  recipes, non-product TOC roots, and ADR edits. The green ratchet runs it as leg B'.
- The generated product surface was regenerated from 108 evaluated packages and 14 conservative claims;
  its focused compiler suite passes 7/7.
- `samples/FirstUse` builds Release with zero warnings/errors. The real FirstUse source contract passes
  1/1, covering the executable first-use journey rather than a prose-only example.
- Documentation lint reports zero errors (1,622 retained historical/frontmatter warnings). The public
  truth gate, diff check, and ADR-immutability check pass; no `docs/decisions/` file changed.
