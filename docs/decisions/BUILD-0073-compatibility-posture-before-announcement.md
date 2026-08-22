---
id: BUILD-0073
slug: compatibility-posture-before-announcement
domain: Operations
status: Accepted
date: 2026-08-22
title: Compatibility posture before announcement
related:
  - BUILD-0072
  - DATA-0119
---

# BUILD-0073: Compatibility posture before announcement

## Context

`Sylin.Koan.*` 1.0.0 is on nuget.org. The framework is not announced, has no published guidance pointing at
those packages, and nothing outside this repository is built on them.

`Directory.Build.targets` nevertheless pinned `KoanTrainBaselineVersion` to `1.0.0` and enabled the SDK's
package validation for every packable assembly. Every removed type became `CP0001`, every removed member
`CP0002`, measured against that baseline.

One stabilization cycle produced **101 such differences** across five packages:

| Package | Differences |
|---|---|
| `Koan.Data.Vector.Connector.Qdrant` | 39 |
| `Koan.Data.Relational.Abstractions` | 34 |
| `Koan.Data.Relational` | 15 |
| `Koan.Data.Abstractions` | 10 |
| `Koan.Data.Relational.Npgsql` | 3 |

Every one was deliberate design: the relational schema orchestrator becoming a single owner (DATA-0119), the
vector adapter surface settling, and one type moving assemblies so a document store could reach the contract
governing it. None was an accident, and none was owed to anyone.

Two properties of the gate made this invisible until a release was attempted. It runs at **Pack** and never at
**Build**, so a removed public member leaves a green solution and an unshippable package. And nothing in the
ordinary development loop packs, so the differences accumulated silently across the whole cycle.

## Decision

**Koan 1.x is the stabilization line. Public surface may be removed inside it, and the compatibility gate is
off until the framework is announced.**

- `KoanHasPublishedBaseline` is `false`. `PackageValidationBaselineVersion` and `EnablePackageValidation`
  therefore resolve empty, and packing validates nothing.
- The 101 differences are **deleted, not suppressed**. Generated `CompatibilitySuppressions.xml` files were
  written and then removed: a suppression is a record of debt, and there is no creditor.
- `KoanTrainBaselineVersion` stays centrally defined at `1.0.0`. The switch governs whether a baseline is
  *enforced*, not whether the train has one.
- `PackageTrainBaselinePolicyTests` asserts the current posture rather than the previous one, so an accidental
  flip fails in either direction, and it carries the instruction for the reversal below.

**At announcement, reverse this.** Set `KoanHasPublishedBaseline` to `true`, set `KoanTrainBaselineVersion` to
the version actually published then, and invert the policy test. From that point a removed public member is a
real cost to a real consumer, and the suppression flow becomes the way to record a deliberate break:

```powershell
dotnet pack Koan.sln -c Debug -p:ApiCompatGenerateSuppressionFile=true
```

A type that merely moves assemblies needs no entry even then — `[assembly: TypeForwardedTo(...)]` keeps the
name resolving, which is cheaper than a suppression and cheaper than the move being visible at all.

## Consequences

- The surface can be cut down while it is still worth cutting down. Dead public members — six pillar
  `Descriptor` properties and five `AssociateNamespace` methods with no callers anywhere — are simply removed
  rather than carried or bookkept.
- **Accidental removals are now unprotected.** Nothing distinguishes deleting something dead from deleting
  something load-bearing until announcement restores the gate. The mitigation is that nothing external can be
  harmed in the meantime; the exposure is entirely internal.
- The reversal has a named trigger and a test that fails if someone performs half of it.
- When the gate returns, it must be run by something that packs. Wiring it into a normal build is not possible
  — package validation is a Pack-time target — so CI has to pack deliberately or the gate protects nothing.
  This is the property that let 101 differences accumulate unseen, and re-enabling the switch alone does not
  fix it.

## References

- `Directory.Build.targets` — `KoanHasPublishedBaseline`, `KoanTrainBaselineVersion`
- `docs/engineering/nuget-publishing.md` — "Breaking something inside 1.x"
- `tests/Koan.Packaging.Tests/PackageTrainBaselinePolicyTests.cs`
- BUILD-0072 (superseded) — script-owned versioning
