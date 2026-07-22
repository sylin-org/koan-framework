---
type: SPEC
domain: framework
title: "R12 - Road to the 0.20 Preview"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: in-progress
  scope: epic charter and dependency-ordered preview maturity plan
---

# R12 — Road to the 0.20 preview

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `in-progress`
- Depends on: passed R09, R10, and R11; completed local R08-01 through R08-05 evidence
- Supersedes: the unexecuted public-observation and upgrade tail of R08
- Unlocks: a coherent public 0.20 package line that external developers can install, evaluate, and
  advance without repository knowledge
- Owner: the complete preview promise across code, packages, public narrative, consumer proof,
  publication, observation, and feedback

## Mandate

The 0.20 line is Koan's public maturity cycle. It does not earn its value by adding another capability
family. It makes the current framework installable, understandable, diagnosable, upgradeable, and
honest enough for people outside the repository to test.

Only packages whose public contracts Koan explicitly guarantees earn the 0.20 quality band. R12-01
selects those guarantees from evidence, maps them to their complete public package/dependency boundary,
and gives each qualifying owner explicit `0.20` compatibility intent. Demonstrated, experimental, and
unassessed packages do not inherit 0.20 merely because they build, pack, or sit in a promoted package's
graph. Each project retains independent local version ownership while the product compiler selects the
supported dependency closure. R12-01 owns the exact NuGet prerelease label, NBGV output,
dependency-band, and compatibility expression before version files change.

## Application intent

> A developer encounters one present-tense Koan story, installs one coherent 0.20 preview, reaches a
> meaningful business result quickly, adds capabilities without architectural reset, understands what
> Koan selected and why, and receives useful correction when an environment cannot satisfy the intent.

The shortest public comparison remains:

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

The preview must preserve this economy as applications progress through persistence, Web, Jobs,
Communication, MCP, security, and optional AI/vector capability.

## Preview laws

1. **0.20 is earned guarantee.** Only an explicitly guaranteed package contract and its defensible
   public boundary reach the 0.20 line; synchronized repository-wide versioning does not return.
2. **Maturity is earned, not implied by publication.** Published packages remain verified,
   demonstrated, experimental, or explicitly unassessed according to evidence.
3. **One public narrative.** Every public-facing document, package page, template, sample, install
   instruction, generated product page, and contribution front door speaks as one greenfield product.
4. **History remains history.** ADRs, initiatives, assessments, and archives retain dated evidence but
   never become alternate public curriculum or leak campaign-era instructions into package pages.
5. **Address means decide.** A concern is closed by correcting it, removing the misleading surface, or
   explicitly excluding it from the preview promise. It is not closed by adding an unproved feature.
6. **Outside-in evidence wins.** Repository suites prove implementation; package-only cold starts,
   upgrades, failures, and independent readers prove the public product.
7. **No release-day reconstruction.** Git, evaluated package identity, project-local NBGV state, the
   product compiler, and ARCH-0110's one `main`-boundary job remain the only release owners; no escrow
   or parallel coordinator returns.

## Dependency-ordered execution

| Child | Meaningful outcome | Status |
|---|---|---|
| [R12-01](r12/R12-01-preview-contract-and-version-band.md) | one exact 0.20 version, channel, guaranteed-package set, compatibility, support, and feedback contract before selective version edits | passed |
| [R12-02](r12/R12-02-preview-blocking-seams.md) | every current PMC is re-evaluated against the proposed guarantee set; promise-level activation, configuration, lifetime, naming, wire, safety, and tooling concerns receive fix/remove/exclude decisions | passed |
| [R12-03](r12/R12-03-preview-product-boundary.md) | a small recommended spine, advanced extensions, experiments, and non-claims are generated from current evidence without equating package availability with support | passed |
| [R12-04](r12/R12-04-coherent-public-narrative.md) | every public-facing surface tells one greenfield, present-tense product story and an anti-drift gate preserves it | passed |
| [R12-05](r12/R12-05-public-consumer-journey.md) | the public template installs from NuGet, generates both starters, restores cleanly, and proves the SQLite-backed consumer result | passed |
| [R12-06](r12/R12-06-publish-and-observe-first-wave.md) | one `main`-boundary job publishes and observes the exact supported package set without a second release system | passed |
| [R12-07](r12/R12-07-preview-evolution.md) | the first new dependency-closed R13 wave upgrades a public-created application, proves interrupted-publication recovery, establishes the feedback baseline, and produces the final R12 go/no-go record | pending |

R12-01 through R12-06 are complete. R12-07 remains one bounded preview-evolution proof and will close
through the first dependency-closed R13 wave. [ARCH-0120](../../../decisions/ARCH-0120-terminal-package-maturity.md)
and [R13](R13-terminal-package-maturity.md) own the longer 55-package terminal-maturity program, so
R12 completion does not wait on every cross-repository handoff.

## Scope

### In

- the exact 0.20 package/version/channel and guarantee-admission contract;
- the previously identified promise-level correctness and safety seams;
- preview support and maturity classification;
- coherent public documentation, templates, samples, package presentation, and generated truth;
- clean package-only first use, progression, corrective failure, and inspectability;
- initial publication, immutable evidence, upgrade/recovery observation, and feedback triage;
- bounded tooling improvements required to make those operations understandable and reproducible.

### Out

- new capability breadth without a direct preview blocker or a proved external consumer;
- restoring the shelved CLI or Aspire families;
- restoring retired authentication factors, invitation, control-plane, or compatibility surfaces;
- presenting every package as equally mature;
- rewriting dated ADRs, initiatives, assessments, or archives into marketing prose;
- private dogfood identity, artifacts, or unverifiable private claims.

## Evidence economy

- Use focused owner/consumer tests for each correction.
- Do not rerun the complete release ratchet after documentation-only or family-local changes.
- Run one complete local candidate boundary after code, product truth, package versions, and narrative
  converge.
- Use isolated local packages for pre-publication candidate proof. Use public NuGet only for the external
  product proof after the complete first wave is visible.
- Perform no remote mutation during planning, assessment, or local implementation. The terminal
  publication child must revalidate the exact target, credential boundary, and irreversible actions
  immediately before use.

## Acceptance

R12 passes only when:

1. every package promoted to 0.20 has an explicit guaranteed contract and coherent dependency boundary,
   while non-promoted packages remain truthfully outside that signal and all owners retain independent lineage;
2. every accepted preview blocker is fixed, removed, or explicitly excluded with truthful correction;
3. public maturity and the recommended product spine are evidence-derived and legible;
4. all public-facing content passes R12-04's coherent-narrative contract;
5. public packages and templates reproduce the documented first result without repository access;
6. the exact initial preview wave has immutable, reconcilable public evidence;
7. a later preview wave proves upgrade and interrupted-release recovery;
8. external feedback is recorded as bounded evidence, not used to reopen architecture by anecdote;
9. the maintainer receives a concise go/no-go record for the next maturity band.

## Stop conditions

- Stop if a package reaches 0.20 because of repository membership, dependency closure, or mechanical
  quality alone rather than an accepted public guarantee.
- Stop if documentation is rewritten before the underlying support/version decision is settled.
- Stop if a public page needs historical initiative context to explain ordinary use.
- Stop if “maturity” becomes a synonym for package existence, test count, or feature breadth.
- Stop before remote mutation unless the exact dependency-closed candidate, target, credential
  boundary, and observable recovery posture are recorded and green under R12-06 and ARCH-0110.
