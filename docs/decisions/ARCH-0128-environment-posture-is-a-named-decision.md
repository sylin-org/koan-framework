---
id: ARCH-0128
slug: environment-posture-is-a-named-decision
domain: Architecture
status: Accepted
date: 2026-08-19
title: Environment posture is a named decision, not a boolean read
related:
  - DATA-0046
  - SEC-0001
  - MESS-0026
---

# ARCH-0128: Environment posture is a named decision, not a boolean read

## Outcome

A capability may not decide whether it composes by reading `IsDevelopment()` or `IsProduction()`
directly. It states which **named decision** it is making, and `KoanEnv.Gate` applies that decision's
law:

| Gate | The question it answers | Unlockable in Production? |
| --- | --- | --- |
| `KoanEnv.Gate.Enforce` / `Allows` / `Announce` (a `KoanMagic`) | May this convenience run automatically? | Yes — the capability's own option, or `Koan:AllowMagicInProduction` |
| `KoanEnv.Gate.DevelopmentOnly` | May this surface exist at all? | **No.** Nothing unlocks it |
| `KoanEnv.Gate.LooksDeployed` | What should the unconfigured default be? | n/a — a default, never an authorization |

Reading `KoanEnv.IsDevelopment` / `IsProduction` remains correct for **diagnostics**: log verbosity,
startup banners, how much detail a health payload carries. Those are descriptions of the environment,
not decisions about capability.

## Context

The environment booleans are honest facts, and nothing was wrong with them. What was missing was
vocabulary. Every call site that needed to gate a capability re-derived, from scratch, *which* fact
answered the question — and the answers drifted apart. A survey found 43 such derivations across 33
files, in at least four mutually inconsistent spellings.

Three were live defects:

1. **Staging was treated as Production, inconsistently.** Auto-DDL gated on `!IsProduction`, so it ran
   in Staging. AI endpoint auto-discovery gated on `IsDevelopment`, so it did not. Nothing articulated
   why the two should differ; they differed because two authors picked different booleans.

2. **A documented escape hatch that no call site consulted.** DATA-0046 §Implementation states that in
   Production, "either `KoanEnv.AllowMagicInProduction` or `AllowProductionDdl` must be true." All
   three relational DDL gates checked only `AllowProductionDdl`. The framework-wide flag was
   documented, configurable, reported in the boot snapshot, and inert.

3. **A safety rail shipped as a functionality block.** An earlier Classification gate refused local key
   custody outside `IsDevelopment()`, which broke Test, Staging, and CI — environments where the
   built-in provider is exactly right. The law is that *Production* is the gate.

The failure mode is not carelessness. It is that "is this Development?" and "may this capability
compose?" are different questions that happen to share a datatype, so a wrong answer type-checks.

A fourth family made the same mistake in the opposite direction, and matters more. Development-only
surfaces — the admin console, the dev token endpoint, seeded credentials, the test auth provider —
must be **absent** outside Development, and SEC-0001 §4.2 requires that gate stay decoupled from
`AllowMagicInProduction`. Folding those into one universal "environment gate" would have made a
convenience flag into an authentication bypass. That is precisely why this ADR names two gates rather
than one, and why they do not share a parameter.

## Decision

**One law per named decision, stated once.**

`KoanMagic` describes a convenience as a value: what it does, what the risk is in Production, and what
the operator should do instead. Those fields are required because a refusal missing any of them is an
outage without a remedy. `KoanEnv.Gate` turns that description into a verdict:

| Environment | Verdict | Behavior |
| --- | --- | --- |
| Development | `Allowed` | Runs silently — the ordinary inner loop |
| Staging, Test, CI, unset | `AllowedWithNotice` | Runs, and warns |
| Production **with** consent | `AllowedByConsent` | Runs, and warns |
| Production **without** consent | `Refused` | Refuses, naming capability, risk, remedy, and the flag |

Consent is the capability's own option **or** `Koan:AllowMagicInProduction`. Either alone suffices:
requiring both would make the framework-wide flag useless, and requiring neither would make Production
indistinguishable from Development.

`DevelopmentOnly` takes no consent parameter and reads no flag. The absence of an override is the
feature; a surface that wants one belongs behind a `KoanMagic` instead, which is a design question to
answer deliberately rather than a call-site workaround.

`LooksDeployed` (Production **or** in a container) picks defaults only. Container detection is
evidence, not proof, so it must never decide whether a request is authorized — only what an
unconfigured setting should be when nobody said.

### Deliberate exceptions

Three sites read the environment directly and are annotated in place, so the next reader can see they
were considered rather than missed:

- **`DataAxisPreflight`** — a confirmed cross-tenant read is not a convenience, so nothing may unlock
  it. Routing it through `KoanMagic` would let `AllowMagicInProduction` unlock a data-isolation leak.
- **`IssuerKeyGuard`** — guards Production *and* Staging (both issue tokens real clients hold), with
  its own dedicated acknowledgement rather than the shared flag.
- **`IssuerKeyRotationService`** — a background schedule with no work to do in Development. No surface
  is being gated, so no gate applies.

The MCP transports' plaintext warning (`IsProduction && !InContainer && !IsHttps` — "production with
no TLS-terminating proxy in front") stays a transport-local heuristic. It is one concept in one pillar
and does not earn a framework name.

### Enforcement

The rule was already written when the drift happened, because a direct `IsDevelopment()` read compiles,
passes review, and looks exactly like correct code. Prose alone does not hold it, so the law is
structural: `EnvironmentGateConformanceSpec` scans `src/` and enumerates every direct environment read
with the reason it is allowed. A new one fails the build with a message that names the three gates and
asks which decision the author is making. Removing one also fails, so the list cannot accumulate
entries that stopped being true.

The allowlist is keyed by file **and count**, so a new gate added inside an already-allowed file is
caught too. `KoanEnv.cs` and `KoanEnvGate.cs` are excluded as the mechanism itself.

## Consequences

Behavior changes, both corrections:

- **Auto-DDL now honors `Koan:AllowMagicInProduction`**, as DATA-0046 always specified. Setting the
  flag in Production previously did nothing for schema creation.
- **AI endpoint auto-discovery now runs in Staging, Test, and CI**, with a warning, instead of being
  silently off. This matches MESS-0026's stated policy ("off by default in Production, on in other
  environments") and removes the unexplained divergence from DDL. Production behavior is unchanged.

Everything else is a refactor: all 43 decision points now say which decision they are making, and a reader
can answer "can this be turned on in production?" by reading the method name.

The gates take an optional `IHostEnvironment`. Passing the injected one keeps the decision testable and
scoped to the host; omitting it falls back to the process snapshot, which is fail-closed when
uninitialized (an unset environment name is neither Development nor Production).

## Evidence

- `KoanEnv.Gate` and `KoanMagic` — `src/Koan.Core/KoanEnvGate.cs`
- `RelationalDdlGate` — one description of auto-DDL shared by three adapters
- `tests/Suites/Core/Koan.Core.Tests/Hosting/EnvironmentGateSpec.cs` — 10 specs pinning the law,
  including that every environment below Production runs the convenience and that a refusal names all
  four of capability, risk, remedy, and flag
- `tests/Suites/Core/Koan.Core.Tests/Hosting/EnvironmentGateConformanceSpec.cs` — the structural
  guard, mutation-verified in both directions: an injected bespoke gate in a new file and a second
  read inside an allowed file each fail with the teaching message
- The law is stated once and routed to, not restated: `CLAUDE.md` architectural laws and
  `.codex/skills/explore/SKILL.md` global constraints each carry one line pointing here
- Full solution builds with 0 warnings; Core (349), Classification (60), Identity (90), MCP
  Conformance (84), MCP Explorer (16), Auth Server (50), Web Admin (13), OpenAPI (12), Tenancy Web
  (13), Data Core (474), Sqlite (48), Relational (18) all green
