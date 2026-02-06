# DATA-0089: Zen Garden connection intent minimum URI shape

Status: Accepted

## Context

Koan is introducing Zen Garden-backed connection intent resolution so applications can declare dependencies without hardcoding infrastructure endpoints.

For this to stay low-friction and consistent across adapters, we need one minimal, canonical URI form that is always valid and easy to remember.

## Decision

1. The minimum valid Zen Garden connection intent URI is:

```text
zen-garden://<offering>
```

Example:

```text
zen-garden://mongodb
```

2. Extended forms are optional and additive. They must not replace or invalidate the minimum shape:

- `zen-garden://<offering>:<instance>`
- `zen-garden://<offering>?cap=<item>[,<item>...]`

3. Capability items in query form are untyped by default (bare names). Typed selectors remain optional for disambiguation only.

4. Resolution semantics (planned integration path):

- When `zen-garden://...` is used, resolver attempts Zen Garden first.
- If Zen Garden resolution fails, connectors fall back to existing autonomous discovery behavior.
- Existing explicit native connection strings remain pass-through.

## Consequences

Positive:

- Stable and minimal onboarding syntax.
- Preserves reference=intent ergonomics with sane defaults.
- Avoids coupling protocol UX to offering-specific capability labels.

Tradeoffs:

- Extended grammar still needs strict parser/validation rules as implementation proceeds.
- Fallback provenance needs explicit logging so operators can distinguish Zen Garden resolution from autonomous fallback.

## References

- `src/Koan.ZenGarden/README.md`
- `src/Koan.ZenGarden/TECHNICAL.md`
- `f:\Replica\NAS\Files\repo\github\zen-garden\docs/guides/tools-domain-user-guide.md`
- `f:\Replica\NAS\Files\repo\github\zen-garden\docs/proposals/zen-garden-spec-tools-domain.md`
