# DATA-0053: Direct is relational-only; adapters own DbConnection creation via factories

Status: Accepted
Date: 2025-08-19

## Context
Direct commands previously created provider-specific DbConnection instances in Core, violating separation of concerns and dragging DB client packages into Core.

## Decision
- Scope Direct to relational-only.
- Adapters own DbConnection creation via a capability interface in Core: `IDataProviderConnectionFactory`.
- Direct resolves connection strings via `IDataConnectionResolver`, then asks the corresponding provider factory to create a DbConnection (unopened). Direct opens and executes with cancellation/timeouts.
- Non-relational adapters are out of scope for Direct; a clear NotSupported is returned.

## Rationale
- Preserves SoC and keeps Core dependency-light.
- Allows providers to apply their own connection settings and quirks.
- Keeps Direct thin and provider-agnostic.

## Implementation
- Added `Koan.Data.Core.Configuration.IDataProviderConnectionFactory` and provider implementations:
  - SqlServerConnectionFactory
  - PostgresConnectionFactory
  - SqliteConnectionFactory
- Updated `Koan.Data.Direct` to resolve factories from DI and remove type-switches.
- Removed/Excluded previous Core-level DirectService implementation that created connections.

## Usage and resolution rules
- Direct("{idOrSource}") resolves:
  1) As a named source via `IDataConnectionResolver` (provider id + connectionString); if found, use that provider id.
  2) Otherwise treat as provider id/alias and expect an unambiguous configured source or `WithConnectionString()`.
- Errors are explicit for: adapter not found, multiple sources for adapter id, missing/invalid connection string, and non-relational provider ids.

## Consequences
- Core has no references to provider client packages.
- Providers register factories via `TryAddEnumerable` to support multi-provider apps.
- Future: add optional `IRelationalDialect` for parameter prefix/type mapping to avoid "@" assumptions.
