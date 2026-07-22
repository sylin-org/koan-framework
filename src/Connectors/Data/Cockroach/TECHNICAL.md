# Sylin.Koan.Data.Connector.Cockroach technical notes

## Ownership

The connector owns CockroachDB provider identity and priority, options/configuration, source connection resolution,
autonomous discovery, health participation, naming limits, and startup reporting. It maps
those decisions into the module-free `Sylin.Koan.Data.Relational.Npgsql` repository mechanism.

It does not reference `Sylin.Koan.Data.Connector.Postgres`. PostgreSQL and CockroachDB can therefore coexist without
one package activating the other.

## Configuration

- `ConnectionStrings:Cockroach`
- `Koan:Data:Cockroach:ConnectionString`
- `Koan:Data:Cockroach:SearchPath`
- `Koan:Data:Cockroach:DdlPolicy` (`NoDdl`, `Validate`, `AutoCreate`)
- `Koan:Data:Cockroach:SchemaMatchingMode` (`Relaxed`, `Strict`)
- `Koan:Data:Cockroach:AllowProductionDdl`

Named Data sources continue to use the standard `Koan:Data:Sources:<name>` routing surface.

## Runtime behavior

Npgsql supplies connection and command transport. The shared relational owner validates or creates the already-resolved
table using the immutable policy for that provider/source route. Cockroach uses `ORDER BY "Id"` as its stable fallback
because it does not expose PostgreSQL's `ctid` system column.

Health remains non-critical and connection-free until Cockroach wins default election or one of its sources is used.
Active-source health opens each same resolved endpoint used by repositories and executes a minimal probe. A failed
active source reports unhealthy with its source name and a redacted correction; optional unused routes do not gate
application readiness.

Provider-bounded streams use numbered offset pages with the Entity identifier as the stable tie-breaker. They bound
each provider request and yield, but do not promise a snapshot, resumability, mutation-safe traversal, or cross-process
continuation.

## Failure modes

Unreachable endpoints surface Npgsql/provider errors. Forbidden production DDL and strict schema mismatches fail
correctively. Unsupported predicates or ordering reject; the connector does not hide an unbounded in-memory fallback.
