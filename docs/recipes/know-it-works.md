---
type: RECIPE
recipe: know-it-works
title: "Know it works, and know why when it doesn't"
domain: observability
status: current
last_updated: 2026-08-19
audience: [ai-agents, developers]
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/recipes/know-it-works.md
gets_you: "Tests through a real host, plus liveness, readiness, and an account of what the app composed."
works_if: "Always. Facts and health arrive with the foundation; the rest is what you choose to add."
costs: "Facts and health cost nothing. Container-backed tests need a container runtime on the machine running them."
ingredients:
  - "one | tests through a real host | Sylin.Koan.Testing, Sylin.Koan.Testing.Hosting"
  - "optional | real backing services in tests | Sylin.Koan.Testing.Containers"
  - "optional | OpenTelemetry export | Sylin.Koan.Observability"
---

# Know it works, and know why when it doesn't

Facts, health, and composition evidence need no package — they arrive with the foundation. This recipe
is about the parts you choose.

## What is already there

Before adding anything, use what exists. An agent that reads these first stops guessing:

| Ask | Address |
|---|---|
| Is the process alive? | `/health/live` |
| Are its dependencies ready? | `/health/ready` |
| What composed, and which provider won? | `/.well-known/Koan/facts` · `koan://facts` |
| What did references compose, and has it drifted? | `koan.lock.json` |

`koan.lock.json` is written at build time and checked in, so composition drift shows up in a diff
without running anything.

## When to add more

- **Tests through a real host** — as soon as behavior matters. A test that mocks the framework proves
  the mock works. Reach for the real host so composition and behavior are proved together.
- **Containers in tests** — when a claim depends on the real store. In-memory substitutes hide exactly
  the filter, paging, and transaction differences that break in production. The cost is honest: the
  machine running the tests needs a container runtime, which shapes CI.
- **OpenTelemetry** — when someone is actually going to look at it. Exporting traces nobody reads is
  cost without benefit; add it when there is a destination and an owner.

The three claims worth proving are always the same, and the second is the one teams skip:

1. **Behavior** — the journey works.
2. **Composition** — the providers you intended actually participated. A green test proves *something*
   answered; it does not prove your composition is the one you meant.
3. **Correction** — a missing dependency, invalid configuration, or denied action fails at the owning
   boundary with a useful next move.

## Assembly

```powershell
dotnet add package Sylin.Koan.Testing
dotnet add package Sylin.Koan.Testing.Hosting
```

Add `Sylin.Koan.Testing.Containers` when a claim needs the real backing service, and
`Sylin.Koan.Observability` when traces have a destination.

Depth: [testing your app](../guides/testing-your-app.md) ·
[operations](https://github.com/sylin-org/koan-framework/blob/v1.0.0/docs/reference/operations/index.md).

## Prove it

Assert on the evidence, not on log text: that the elected provider is the intended one, that readiness
goes red when a dependency is genuinely unavailable, and that secrets are redacted wherever
composition is projected.

Also assert that no development identity, disposable store, or hidden fallback can present itself as
the production path. That substitution is what makes a suite pass while production fails.

## Boundaries

- Health is not a metric system, and facts are not an audit log.
- Compilation supports a proof; it never replaces one.
- Coverage is not evidence. A journey nobody ran is not proved by a percentage.

## Interacts with

**Everything.** Each recipe's Prove section names the narrow evidence for its own claim; this one is
where the machinery to run it lives.
