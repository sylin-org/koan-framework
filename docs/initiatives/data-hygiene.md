---
type: PLAN
domain: data
title: "Field hygiene attributes ([Trim] / [Lowercase] / [Uppercase])"
audience: [maintainers, framework-authors]
status: accepted
last_updated: 2026-08-26
framework_version: v1.0.0
---

# Field hygiene attributes

**Problem.** String hygiene (trim, casing) is hand-rolled in every application — in setters, in
controllers, or worse, not at all, so `[MatchKey]` values with trailing spaces silently split
identities.

**Decision.** Ship hygiene as a small opt-in module (`Sylin.Koan.Data.Hygiene`) riding the
**existing Plane B field-transform chassis** (`IFieldTransformContributor` →
`StorageFieldTransformPlan`), exactly as the Classification axis does (ARCH-0098 precedent).
Attributes live in `Koan.Data.Abstractions.Annotations` beside `[Index]` so models reference them
with zero package weight.

- `[Trim]` — `string.Trim()` · `[Lowercase]` — invariant lower · `[Uppercase]` — invariant upper.
  String properties only; non-writable properties are skipped at scan time. `null` stays `null`.
- Applied on **write, on a clone** (callers never see corrupted values — ARCH-0098 discipline).
  `ApplyOnRead` is identity for these transforms (nothing to reverse).
- L2-cache exclusion and manifest facts come free from the chassis (`HasTransformsFor`).
- **Rejected**: Plane A write-stamp (in-place mutation silently changes the caller's instance —
  wrong default for hygiene); MVC/DataAnnotations validation (suppressed `[ValidateNever]` on
  entity endpoints, and validation is not normalization); `[Phone]` (E.164 decisions deferred —
  noted as follow-up).
- **Canon parity follow-up** (not this slice): teach `DefaultIntakeContributor` to consult the
  same attribute metadata so match keys are normalized before matching.

**Specs**: Data.Core pipeline spec (scan/compile/skip/apply, caller-instance purity, null pass-through),
plus a SQLite round-trip (write → reload → normalized value stored).

**Out of scope**: `[Phone]`, culture-aware casing, non-string transforms.
