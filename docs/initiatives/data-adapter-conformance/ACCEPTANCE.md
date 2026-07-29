---
type: SPEC
domain: data
title: "Data Adapter Conformance Acceptance Gate"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: work-item and epic acceptance protocol
---

# Data Adapter Conformance Acceptance Gate

The orchestrating session judges each card. An implementer's report is evidence, not a verdict.

## Responsibility separation

- **Gold harvest card:** may inspect the current adapter and provider, but emits only sanitized facts, external contract
  inventory, black-box scenarios, negative lessons, and a retirement manifest. It makes no KEEP/LOCALIZE/code-placement
  recommendation and does not change production.
- **Gold replacement card:** starts from an empty adapter implementation and derives its design from ratified
  Framework/Family contracts, provider facts, public compatibility decisions, negative lessons, and black-box cases.
  It never copies or preserves the retired implementation's internal structure.
- **Gold review card:** verifies the complete replacement, retirement inventory, native behavior, architecture, and
  absence of bridges or dead paths. It emits requirements and black-box cases, not transplant advice.
- **Audit card:** may write only initiative/evidence artifacts and tests explicitly scoped as characterization. It
  does not change production behavior.
- **Remediation card:** consumes frozen scorecard rows and changes exactly one semantic owner.
- **Certification card:** independently reruns evidence and reconciles truth. It does not repair failures it judges.

A fleet `audit-certification` card is one reusable prompt with two read-only invocations, not one blended role. The
first reviewer freezes the audit packet and creates bounded remediation cards for any RED. After all prerequisites are
green, a different reviewer reruns the card from a clean provider fixture as certifier. Only that second invocation may
mark the ledger row `passed`, even when the first audit found no RED.

## Gold replacement boundary

SQLite and MongoDB use a stricter profile than ordinary fleet remediation:

1. A harvester records `L-*` provider lessons and black-box requirements without proposing a new class graph, file
   map, helper set, control flow, cache, or test-fixture design.
2. DAC-15 freezes the ratified public/package/configuration contract, sanitized lessons, and black-box scenarios before
   either target implementation is emptied and rebuilt.
3. The replacement is authored from empty source files. Copying, porting, mechanical transformation, old/new dual
   paths, `Legacy`/`V2`/`Compat` shims, feature-flag fallback, and shadow registration are automatic failures.
4. Continuity is limited to explicitly ratified package/assembly identity, public surface, configuration keys, and a
   revalidated provider dependency. Those are contracts to reimplement, not code to preserve.
5. DAC-13/DAC-24 review each complete deletion+new-source change; DAC-23 composes them and proves one
   registration, repository, execution path, and test authority per gold adapter. The retirement manifest must resolve
   every old file, type, registration, helper, option, and adapter-specific test to absence.
6. Independent review may compare observed behavior to identify a missed valid public requirement or surviving legacy
   path. A missed behavior becomes a black-box requirement; old code is never copied back.
7. Before production authoring, the replacement freezes a compact design inventory of every runtime type, cache,
   background task, resource owner, native dispatch boundary, and abstraction. Each item must own a necessary contract
   boundary; unexplained indirection or warm-path structural work is RED.

## Card gate

Every applicable layer must pass.

### 1. Scope and mechanics

- The diff is confined to the card and pre-existing unrelated changes are untouched.
- Every changed path appears in the card's exact allowlist; every other semantic owner/path is forbidden.
- The source under test reproduces from an authorized commit or sealed base-commit + patch + source-manifest checkpoint.
- Focused affected projects build and focused tests run green.
- The card's required real-provider tests execute; skipped LIVE cells are not PASS.
- `git diff --check` is clean on the composed replacement bundle.
- Documentation links/front matter and changed code examples pass repository lint when applicable.
- No new warning, silent catch, text-prefix effect inference, sync-over-async bridge, or unbounded cache is introduced.
- For a completed gold replacement, the DAC-13/DAC-24 bundle contains the complete legacy deletion set and complete
  new implementation; no superseded compile item, registration, fallback, fixture, or unreachable helper remains.
- Gold evidence proves an empty implementation start, one final execution path, complete retirement, and no compatibility
  bridge or shadow registration.

### 2. Primer traceability

- Every finding and new test names a stable primer acceptance ID and atomic case.
- Required evidence kinds are conjunctive; none is silently dropped.
- The claim-to-cell matrix has no advertised claim without rows and no executable surface absent from inventory.
- Declined capabilities have negative proof through every alternate path.
- A capability token and its conformance verifier land together.
- The primer, including ratified annexes, remains the only semantic acceptance catalog.

### 3. Ownership and architecture

- Framework policy/plan/result behavior lives in Data.
- Repeated family mechanics live in the narrow Family substrate.
- Provider code contains only native translation, resource ownership, dispatch, and exact native failure mapping.
- Mixed cases have linked Framework/Family/Adapter rows and execution receipts.
- A Framework RED is not closed by an adapter-local option, duplicate gate, or copied materializer.
- A gold replacement demonstrates this ownership through a new implementation against shared seams, not by reshaping
  the former adapter.

### 4. Behavioral evidence

- STATIC/BOOT/ORACLE/LIVE/NEG/FAULT/PLAN/LIFE/PERF evidence required by the selected rows is reproducible.
- Fault and cancellation evidence distinguishes caller cancellation, provider timeout, pre-commit failure, and
  outcome unknown.
- Native plan/trace evidence proves handled filter, sort, paging, count, index, and bulk claims.
- Two-host, disposal, restart, and soak cells prove isolation and resource cleanup.
- Provider fixtures pin version and least-privilege posture; required infrastructure failure is RED/DEFER, never skip-green.

### 5. Test adequacy

- New behavior has a failing-before/passing-after proof or equivalent mutation check.
- Shared semantic tests run through a real `AddKoan()` host.
- Provider-specific tests supplement rather than weaken shared cells.
- A coverage critic identifies missing negative, alternate-path, fault, and boundary cases.

### 6. Performance and explainability

- Structural warm-path work is absent and guarded.
- Applicable benchmarks record allocation, provider dispatch count, elapsed time, and native work against a pinned
  provider-relative baseline.
- `Describe`, `Explain`, `Doctor`, facts, health, errors, and README claims agree with the executable plan.
- Secrets, business values, full native errors, and high-cardinality identifiers remain outside public diagnostics.

### 7. Truth reconciliation

- The evidence packet satisfies primer §10.7.
- A gold packet additionally resolves empty-root, legacy-retirement, replacement architecture, and behavioral evidence.
- Runtime claims, product claims, generated product surface, README/TECHNICAL limits, and tests resolve to the same
  claim set.
- Existing supported status is blocked when evidence is RED/DEFER. Downgrade, withdrawal, a new Target/Declined choice,
  or a non-shipping disposition requires recorded human product approval and a separately pinned identity.
- `PROGRESS.md` and `NOW.md` identify the exact next safe action.

## Verdicts

- **PASS:** every required layer is green and the card's definition of done is complete.
- **BLOCK:** an in-scope, actionable prerequisite or test failure prevents completion.
- **STOP:** scope, authority, safety, or architecture is ambiguous; a human or predecessor decision is required.

Partial implementation is never PASS. A RED audit can itself PASS when it completely and reproducibly records the
RED findings required by its card.

## RED return protocol

When an audit-certification or certification gate is RED, the orchestrator freezes the failing rows, creates bounded
one-owner remediation cards with exact paths, and queries packet dependencies. For DAC-12/DAC-22, a correction changes
only the new implementation against the failing black-box case, reruns DAC-13/DAC-24, reseals DAC-23, and reruns both
certifications. Copying, mechanical transformation, or preserved legacy internal structure is not a correction: empty
the target and rerun DAC-11/DAC-21. An incomplete retirement inventory returns to harvest/DAC-15 before recomposition.
Every verdict consuming the changed owner/path/profile/tool/fixture becomes stale, including upstream and sibling packets;
all affected certification cards
become re-entry dependencies of the failed gate. The certifier never fixes the failure. After remediation, different
reviewers rerun every invalidated gate from one new sealed checkpoint; prior evidence remains traceable but cannot
supply the new verdict.

## Certification boundaries

- **Foundation:** focused Data Core/Abstractions/Relational/TestKit builds and suites, then full `Koan.sln` build.
- **Gold:** the complete real SQLite or Mongo suite, strict Forge run, packet validation, legacy-absence proof,
  independent source-lineage review, and independent behavioral review.
- **Fleet adapter:** its complete real-provider suite plus shared applicable cells and strict Forge run.
- **Portfolio:** fresh dynamic roster, every packet, generated product-surface check, full solution build, tiered CI
  commands, documentation lint, and privacy review.

## Epic verdict

The epic is complete only if DAC-99 can derive every shipped Data adapter from the repository and resolve it to a
green packet. A missing adapter, orphan claim, skipped LIVE cell, unresolved evidence reference, or unsupported
advertised capability blocks completion.
