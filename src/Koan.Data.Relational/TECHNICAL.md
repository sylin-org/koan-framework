# Sylin.Koan.Data.Relational technical notes

## Ownership

`RelationalModule` is the one functional owner of schema orchestration. Concrete providers reference this package and
receive that service through normal Koan composition; they do not register bridges or mutate shared relational options.

Every repository passes the already-resolved table plus an immutable `RelationalSchemaPolicy` to the orchestrator.
That policy carries projection mode, DDL policy, matching strictness, production guard, and provider schema. This makes
schema decisions local to the selected provider/source route and prevents one connector from changing another.

Provider implementations supply `IRelationalDdlExecutor`, `IRelationalStoreFeatures`, and `ILinqSqlDialect` from the
module-free Abstractions package. Application code should not consume these contracts.

## Schema behavior

- `NoDdl`: inspect only; missing schema is reported and creation is rejected.
- `Validate`: inspect only; callers can use the report as a readiness or corrective signal.
- `AutoCreate`: create a missing table and add missing projected columns when the environment guard allows it.
- `Relaxed`: a mismatch reports `Degraded`.
- `Strict`: a mismatch reports `Unhealthy`; repositories surface their corrective schema error.

Creation is additive. Koan does not rename/drop columns, infer destructive migrations, or promise full migration
management. Production DDL remains denied unless the selected provider explicitly allows it.

## Query translation

The common translator supports scalar equality/comparison, logical composition, null checks, Boolean members, and the
basic string operations providers can represent safely. Provider dialects own quoting, parameters, LIKE escaping, and
JSON-array operations. Unsupported expressions fail closed; Koan does not silently scan an unbounded source.

Provider-specific SQL, connection lifecycle, discovery, health, and startup reporting remain in each concrete connector.
