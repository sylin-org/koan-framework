# Migration from Zen to Sora

Sora modernizes and modularizes the former Zen framework. Key notes:

- Namespaces: move to `Sora.*` packages.
- Data: annotate aggregates with `Identifier` and `Index` (property-anchored).
- Relational: complex properties stored as JSON; ensure hydration logic.
- Dialects: move to provider packages (e.g., SqliteDialect -> Sora.Data.Sqlite).
- Instruction API: replace custom escape hatches with `Instruction` + `Execute`.

Strategy:
- Migrate one feature at a time; add adapter tests.
- Use ADRs to document deviations from Zen.
