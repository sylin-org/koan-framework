title: Sylin.Koan.Data.Vector.Connector.SqliteVec - Technical Reference
description: Embedded durable sqlite-vec provider for Koan.
packages: [Sylin.Koan.Data.Vector.Connector.SqliteVec]
source: src/Connectors/Data/Vector/SqliteVec/

## Composition and election

The package references `Sylin.Koan.Data.Vector`, so one connector reference supplies its functional runtime.
`SqliteVectorModule` registers one singleton `SqliteVecAdapterFactory`, exposes it through `IVectorAdapterFactory`, and
registers one participation-aware health contributor. Provider identity is `sqlitevec`; aliases `sqlite` and
`sqlite-vec` let Vector election pair it with the SQLite record provider. Priority is `40`.

## Placement decision

`SqliteVecRoute` is the single side-effect-free placement owner used by repository construction, readiness, and startup
projection. Precedence is:

1. source-specific sqlite-vec placement;
2. `Koan:Data:SqliteVec:ConnectionString`;
3. `ConnectionStrings:SqliteVec`;
4. source-specific or default SQLite placement;
5. `Koan:Data:Sqlite:ConnectionString`;
6. `ConnectionStrings:Sqlite`;
7. `Data Source=.koan/data/Koan.sqlite`.

Generic source connections are consumed only when source ownership matches the factory's declared identity/aliases.
Route calculation creates no directory and opens no connection. Repository use or active readiness owns those effects.

## Repository and isolation

Vector Core memoizes one repository per entity/source and disposes it with `VectorService`. The repository holds one
open `Microsoft.Data.Sqlite` connection, loads vec0, and serializes operations through a semaphore. It creates one
`vec0` virtual table per `VectorAdapterNaming` result, so provider-selected entity, ambient partition, and non-default
source folds share the framework naming policy without re-election.

The provider declares kNN, bulk upsert/delete, atomic batch, score normalization, and dynamic collections. It does not
declare metadata filters. `ScopedVectorRepository` therefore fails closed when tenant/Shared read isolation would
require filter pushdown. Database-mode sources can route to distinct files and are also source-folded in table names;
partition isolation uses distinct tables.

## Native loading

vec0 v0.1.9 is embedded for win-x64, linux-x64, and linux-arm64. On first selected use, `Vec0Native` hashes the embedded
payload, validates any versioned cached extraction, writes a unique temporary file when repair is needed, and moves it
into place before loading the `sqlite3_vec_init` entry point. Unsupported platforms fail before a repository operation
can imply availability.

## Health

`SqliteVecHealthContributor` derives from the Vector participation base. With no active source it is non-critical and
returns `Unknown` without filesystem or native work. After selection it resolves the same route as the factory, opens
the database, loads vec0, executes `SELECT 1`, and reports all active source identities. Probe errors are de-identified
by the shared health base.

