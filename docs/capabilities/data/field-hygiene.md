---
type: REFERENCE
domain: data
title: "Field hygiene"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-26
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-26
  status: passed
  scope: docs/capabilities/data/field-hygiene.md - unit-verified against the transform-plan host
    (Koan.Tests.Data.Core HygieneTransformSpec): trim/casing applied to the persisted clone,
    caller instance untouched, null/empty passthrough, non-hygiene types excluded.
---

# Field hygiene

Normalize annotated Entity string properties before persistence — trim whitespace, enforce casing —
without touching job code, controllers, or setters.

## You need

| Piece | Package | Note |
|---|---|---|
| Hygiene attributes and engine | `Sylin.Koan.Data.Hygiene` | Reference = Intent — referencing the package is the whole step |
| Attribute declarations | `Koan.Data.Abstractions` (already referenced by every Entity app) | `[Trim]`, `[Lowercase]`, `[Uppercase]` on string properties |

## The constraint box

> **The constraint:** Hygiene normalizes the **persisted clone** on every write path — the caller's
> in-memory instance is never modified (ARCH-0098 clone discipline). Normalization is applied
> verbatim, so a value that *becomes* clean only after an application-level change must be cleaned
> by the application: hygiene is mechanical, not validation. `null` stays `null`; empty strings stay
> empty. `ApplyOnRead` is an identity no-op — trimming is irreversible by design, so the stored
> value IS the value.

## Use it

```csharp
using Koan.Data.Abstractions.Annotations;

public sealed class Contact : Entity<Contact>
{
    [Trim, Lowercase] public string Email { get; set; } = "";
    [Trim] public string DisplayName { get; set; } = "";
    [Trim, Uppercase] public string Region { get; set; } = "";
}

await new Contact { Email = "  Ada@Example.COM ", DisplayName = "  Ada  ", Region = " eu " }.Save(ct);
// persisted: "ada@example.com", "Ada", "EU" — the in-memory instance still holds the original text
```

## Route by need

| Need | Use |
|---|---|
| Strip leading/trailing whitespace | `[Trim]` |
| Enforce invariant lowercase (emails, slugs, keys) | `[Lowercase]` |
| Enforce invariant uppercase (region codes, country codes) | `[Uppercase]` |
| Combine (trim then case) | stack attributes: `[Trim, Lowercase]` — trim applies first |
| Ask what the engine did | composition facts name the module; L2-cache is automatically excluded for transformed types |

## Do not, at this level

- Do not use hygiene as validation — it never rejects; it normalizes. Business rejection belongs to
  application rules (or a future validation seam).
- Do not expect the in-memory instance to change after `Save()` — read the persisted value back if
  you need the normalized form.
- Do not annotate non-`string` properties — they are skipped silently at scan time.

## Read next

- [Entity capability hooks](entities.md) — the full hook map around `Entity<T>`
- Design record: `docs/initiatives/data-hygiene.md`
