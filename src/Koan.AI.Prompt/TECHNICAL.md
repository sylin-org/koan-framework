# Sylin.Koan.AI.Prompt — technical contract

## Responsibility

This package owns the optional persistence boundary for Koan prompt values. `PromptEntry : Entity<PromptEntry>` is the
stored record; `PromptCatalog` is the read facade. Prompt parsing, composition, interpolation, examples, and output
shape live in inert `Sylin.Koan.AI.Contracts` so the functional AI runtime has no Data dependency.

## Lookup behavior

- `Load(name)` queries active entries and returns the highest `Version`.
- `Load(name, version)` queries the exact name/version pair without applying lifecycle filtering.
- Names are trimmed at the facade boundary; blank names and versions below one reject before Data access.
- Missing results throw `PromptNotFoundException` rather than returning an empty prompt or falling back to code.
- More than one Entity at the selected name/version is corrective ambiguity and throws before a prompt is returned.

The catalog uses the ambient Entity context, including configured source, tenancy, and other compiled Data
contributors. It introduces no independent provider election, cache, or service locator.

## Runtime and failure boundaries

The package module registers no parallel catalog service. It reports catalog availability at startup; Entity/Data own
storage activation and execution. Query translation, isolation, consistency, and failure behavior remain bounded by
the selected Data provider. Persisted prompt text may contain sensitive information; this package performs no
encryption, redaction, authorization, or content-policy enforcement.
