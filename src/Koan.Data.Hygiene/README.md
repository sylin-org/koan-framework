# Koan.Data.Hygiene

Declarative field hygiene for Koan Entities: annotate string properties with `[Trim]`,
`[Lowercase]`, or `[Uppercase]` and the value is normalized on the persisted clone across every
write path — `Save`, bulk, batch, REST endpoints, soft-delete re-persists.

```csharp
using Koan.Data.Abstractions.Annotations;

public sealed class Contact : Entity<Contact>
{
    [Trim, Lowercase] public string Email { get; set; } = "";
    [Trim] public string DisplayName { get; set; } = "";
}
```

Reference = Intent: referencing the package is the whole step — no registration, no options.

## The contract

- Normalization applies to the **persisted clone**; the caller's in-memory instance is never
  modified (ARCH-0098 clone discipline).
- `null` passes through; empty strings pass through; normalization is invariant-culture.
- `ApplyOnRead` is an identity no-op — trimming is irreversible by design, so the stored value IS
  the value. Types with hygiene transforms are automatically excluded from the distributed L2 cache.
- Non-`string` or setter-less properties carrying hygiene attributes are skipped silently at scan
  time. Hygiene never throws; it is not validation.

Capability node: [Field hygiene](../../docs/capabilities/data/field-hygiene.md) ·
Design record: `docs/initiatives/data-hygiene.md`.
