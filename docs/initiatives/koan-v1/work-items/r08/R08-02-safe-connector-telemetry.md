---
type: SPEC
domain: framework
title: "R08-02 - Make Connector Telemetry Safe by Construction"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: shared sink/discovery mutation proofs, repository bypass gate, and focused affected-connector builds
---

# R08-02 — Make connector telemetry safe by construction

- Tranche: `T7B — V1 release readiness / safe public surface`
- Status: `passed`
- Depends on: R08-01 and passed R09
- Unlocks: one canonical package/provider/maturity product-surface compiler
- Owner: Core logging/redaction boundary; shared adapter/discovery templates own configuration and discovery narration

## Meaningful outcome

Referencing a connector cannot make Koan's own configuration, discovery, health-selection, or startup logs
emit raw credentials. Safe endpoint structure remains useful to a developer or operator, but URI user-info,
passwords, tokens, signed query parameters, and credential-bearing exception prose are masked once at the
shared logging boundary.

Application developers write nothing new:

```csharp
builder.Services.AddKoan();
```

The guarantee follows from the referenced connector. There is no redaction option, per-adapter switch, or
application-owned logger wrapper. A malformed credential-shaped value fails closed as `(masked)`. A connector
that bypasses the safe configuration/discovery boundary fails the focused repository policy gate and is not
eligible for the public release surface.

## Focused exploration

**Task:** centralize credential-safe connector telemetry before any public package promotion.

**Application intent:** “Use a Koan connector and receive useful startup/diagnostic logs without disclosing
the credentials that made the connector work.”

**Public expression:** an ordinary connector reference plus `AddKoan()`; no decoration, configuration,
context, or runtime prerequisite is added beyond the connector's own backend requirements.

**Guarantee/correction:** Koan-owned connector configuration/discovery logs redact credential-shaped strings,
URIs, and exception content. Unsafe source paths reject at the release gate; ambiguous runtime values mask
rather than guessing.

**Public concepts:** none. Logging safety is framework policy, not application vocabulary.

### Docs read

- `docs/engineering/index.md` — requires centralized conventions, focused validation, and release-safe
  package metadata; directly relevant.
- `docs/architecture/principles.md` — makes reference-as-intent, fail-loud behavior, one canonical path, and
  fewer decision owners binding; directly relevant.
- `docs/initiatives/koan-v1/CAPABILITIES.md` — separates verified capability behavior from package support;
  relevant to avoiding an accidental maturity promotion.
- `docs/initiatives/koan-v1/work-items/R08-v1-release-readiness.md` — names connector-wide redaction as the
  first safe-public-surface gate; directly relevant.
- `tools/Koan.Packaging/README.md` and source inventory command — establish the evaluated 108-package graph;
  relevant to the later product-surface compiler, not the runtime repair itself.

### Code read

- `src/Koan.Core/Redaction.cs` — already owns a strong fail-closed credential grammar for connection strings,
  URI user-info, signed query parameters, embedded URIs, and malformed assignments; keep and reuse.
- `src/Koan.Core/Logging/KoanLog.cs` — already owns the structured framework-log chokepoint but currently
  forwards raw context; rebuild this boundary to sanitize once.
- `src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs` — already owns candidate ordering, health
  invocation, result logging, and corrective failures; absorb duplicate adapter health catches/logs here.
- `src/Koan.Core.Adapters/Configuration/AdapterOptionsConfigurator.cs` — already owns adapter configuration
  lifecycle and is the correct shared authoring path; keep connector decisions on `KoanLog`.
- SQLite's options/discovery/lifecycle and redaction tests — closest current safe pattern; retain its behavior
  while removing redundant call-site sanitization after the shared sink proves equivalent safety.

### Inventory findings

- The evaluated graph contains 108 independently versioned packages: 2 bundles, 9 kernel packages,
  96 periphery packages, and 1 template package without a `KoanPackageKind`. All declare package READMEs.
- A conservative source inventory found 62 files with security-relevant logging candidates: 115 direct
  `ILogger` calls and 45 `KoanLog` calls involving connection, endpoint, URL, host, or exception values.
  Only eight of those files use local redaction today.
- Thirteen production discovery adapters derive from `ServiceDiscoveryAdapterBase`; nearly all duplicate
  provider health success/failure logging and catch exceptions that the base already catches safely.
- Older Data/vector configurators log raw initial and final connection values at Information level. Newer
  Mongo, SQLite, Weaviate, and related paths use structured `KoanLog`, but that sink does not itself sanitize.
- Provenance/facts already sanitize independently and remain separate information products; this slice does
  not route runtime logs through provenance or facts.

## Coalescence decision

- **Closest pattern:** SQLite plus `ServiceDiscoveryAdapterBase` and `Redaction.DeIdentify`.
- **Specificity:** credential grammar and structured log safety are framework law; configuration/discovery
  narration is the adapter-family template; provider health mechanics stay in each adapter.
- **Keep:** `Redaction` as the single credential grammar; provider-specific connection and health mechanics.
- **Absorb:** string/URI/exception context sanitization into `KoanLog.Write`; health attempt/outcome/failure
  narration into `ServiceDiscoveryAdapterBase`; configuration lifecycle into `AdapterOptionsConfigurator`.
- **Rebuild:** connector configuration/discovery call sites onto structured `KoanLog` decisions.
- **Delete:** provider health catch-and-log duplicates, raw initial/final connection dumps, redundant
  call-site redaction at the shared sink, and the unused raw `LogKoanDiscover` helpers.
- **Do not create:** a redaction interface, adapter option, logger provider, per-provider sanitizer, or
  application API. Microsoft logging remains the substrate; Koan centralizes only its own framework policy.

## Exact placement

| Change | Location | Why here |
|---|---|---|
| sanitize structured context once | `src/Koan.Core/Logging/KoanLog.cs` | every canonical Koan structured log crosses this existing boundary |
| retain/extend credential grammar only if a red test requires it | `src/Koan.Core/Redaction.cs` | one security parser already owns the meaning |
| own health failure narration | `src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs` | it invokes every provider health check and already owns timeout/failure policy |
| migrate provider decisions | affected `src/Connectors/**` configuration/discovery files | adapters retain mechanics but no longer own security policy |
| runtime and structural safety proof | Core Unit redaction/logging specs plus focused connector tests | prove behavior and fail-closed mutations |
| repository bypass gate | `tests/Koan.Packaging.Tests` | public promotion already depends on this repository-aware evidence owner |
| architecture and contributor guidance | ARCH-0117 and logging engineering guide | make the rule discoverable to humans and coding agents |

## Ergonomics

- Application developer: zero new code/configuration and no leaked secret when a backend fails.
- Connector author: use the existing structured `KoanLog` path; the base owns repetitive health narration.
- Coding agent: one searchable logging boundary and one corrective policy test instead of provider folklore.
- Operator/reviewer: useful provider/method/outcome and de-identified endpoint shape remain visible; raw
  credentials and exception dumps do not.

## Focused acceptance

1. Core redaction tests prove connection strings, credentialed/signed URIs, embedded values, exception prose,
   `Uri`, safe strings, and non-string structured values at the actual `KoanLog` sink.
2. Shared discovery proof injects a credential-bearing candidate and exception; result remains usable while
   every emitted log context is safe.
3. Connector startup, configuration, discovery, orchestration, and health source uses `KoanLog`; direct
   `ILogger.Log*` cannot re-enter the bounded security-sensitive files without failing the repository policy test.
4. Representative Data, vector, Communication, AI, and Storage connector tests/builds pass. No connector-wide
   release certification is run.
5. Active logging docs and ARCH-0117 state the exact boundary and non-claim: Koan does not sanitize arbitrary
   application or third-party library logs.
6. `git diff --check`, privacy, and changed-doc checks pass. No publication occurs.

## Closure evidence

- `KoanLog.Write` now de-identifies structured string, `Uri`, and exception values once before both
  diagnostic capture and Microsoft logging dispatch. Non-string values retain their type.
- `ServiceDiscoveryAdapterBase` owns health timeout/failure narration and normalization fallback evidence;
  provider adapters retain only protocol mechanics. `AdapterOptionsConfigurator` owns safe configuration
  and discovery decision verbs, while the shared orchestration evaluator owns credential-probe narration.
- The focused runtime proof passes 28/28 Core redaction/discovery cells, including a credential-bearing
  health exception. The packaging policy proof passes 1/1 and rejects direct `ILogger.Log*` use in connector
  initialization/discovery folders, options configurators, orchestration evaluators, and health contributors.
- Seventeen affected connector projects across AI, Data, vector, Communication, and Storage build successfully.
  Existing XML/nullability/analyzer warnings in unrelated source remain visible; this slice introduced no
  compile errors and did not run release certification.
- The old raw `LogKoanDiscover` helpers and Redis's local credential grammar are deleted. No application API,
  option, logger provider, adapter security interface, activation metadata, or compatibility crutch was added.
- [ARCH-0117](../../../../decisions/ARCH-0117-safe-connector-telemetry.md) and the engineering logging guide
  state the exact guarantee and its non-claim. PMC-019 is resolved by this child.

## Constraints satisfied

- No Entity/data-access, HTTP route, options/configuration, or application API change.
- No magic security vocabulary per provider; existing structured actions and one Core grammar are reused.
- No hot-path provider negotiation or new runtime registry; sanitization occurs only when Koan emits a
  structured log event.
- Docs remain instruction-first and the structural decision is recorded.

## Risks and stop conditions

- Stop if the design attempts to decorate or replace all Microsoft/application logging; the guarantee is
  Koan-owned connector telemetry only.
- Stop if useful safe endpoint structure is discarded universally when `Redaction` can prove it safe.
- Stop if provider mechanics move into Core or Core learns provider-specific credential syntax.
- Stop if a source-only gate substitutes for runtime mutation proof.
- Stop before the product-matrix compiler, template/upgrade work, release certification, publication, push,
  tag, release, or remote mutation.
