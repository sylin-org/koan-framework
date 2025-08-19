# ADR 0041: GraphQL module (Sora.Web.GraphQl) — controller-hosted schema from IEntity<>, typed filters/sorts, display field

Date: 2025-08-19

Status: Accepted

## Context

- Sora’s `EntityController<T>` delivers low-friction CRUD over `IEntity<>`, with shared hooks (`QueryOptions`, `HookRunner`) and repository abstractions.
- Teams also want GraphQL for flexible selection sets and a single endpoint, while keeping the same domain model, policies, and limits.
- Goals:
  - Add a first-class GraphQL surface that reuses repositories, `QueryOptions`, and hooks to keep behavior aligned with REST.
  - Zero boilerplate per entity: discover `IEntity<>` types and generate schema automatically.
  - Provide typed filter/sort inputs that map to the same `QueryOptions` (ADR-0029/0031/0032), not a separate provider-specific pipeline.
  - Expose a `display: String` field mirroring `EntityController.GetDisplay`.
  - Respect Sora guardrails: Controllers (no inline endpoints), centralized constants, self-bootstrapping.

## Decision

- Add a new module: `Sora.Web.GraphQl`.
- Host via MVC controller: `GraphQlController` at `POST /graphql`. Avoid inline `MapGraphQL` endpoints.
- Use Hot Chocolate as the GraphQL engine (code-first):
  - Packages: `HotChocolate`, `HotChocolate.AspNetCore`, `HotChocolate.Execution`, `HotChocolate.Types`, `HotChocolate.DataLoader`.
  - Rationale: mature .NET integration, dynamic descriptors for schema generation, first-class dataloaders, depth/complexity guards, error extensions.
- Schema generation from discovered `IEntity<>` types:
  - At startup, enumerate loaded types implementing `IEntity<TKey>`.
  - For each `TEntity`:
    - Register `ObjectType<TEntity>` adding `display: String` via a display provider.
    - Query:
      - `<entitySingular>(id: ID!): <Entity>`
      - `<entityPlural>(filter: <Entity>FilterInput, sort: [<Entity>SortInput!], page: Int, size: Int, set: String): <Entity>Connection`
    - Connection: `{ items: [<Entity>!]!, totalCount: Int!, pageInfo { page: Int!, size: Int!, hasNext: Boolean! } }`.
    - Mutation (enabled by default): `upsert<Entity>(input: <Entity>Input!): <Entity>`, `upsert<Entities>(inputs: [<Entity>Input!]!): [<Entity>!]!`, `delete<Entity>(id: ID!): Boolean!`, `delete<Entities>(ids: [ID!]!): Int!`.
- Typed filters/sorts map to `QueryOptions`:
  - `<Entity>FilterInput` with operators per field:
    - String: `equals`, `notEquals`, `contains`, `startsWith`, `endsWith`, `in`, `notIn`, `isNull`, `ignoreCase` (bool)
    - Numeric/Date: `equals`, `notEquals`, `gt`, `gte`, `lt`, `lte`, `between`, `in`, `notIn`, `isNull`
    - Boolean: `equals`, `isNull`
    - Logical: `and`, `or`, `not`
  - `<Entity>SortInput { field: <Entity>SortField!, direction: ASC|DESC }` with enum of allowed fields.
  - Resolver path: convert typed inputs into `QueryOptions` (Filter + Order), then run `HookRunner.BuildOptionsAsync` and `HookRunner.BeforeCollection`. Do not apply provider-specific IQueryable middleware.
- Display provider parity:
  - `IEntityDisplayProvider<TEntity>` computes `display` (prefer Name/Title/DisplayName; fallback to Id). Allow DI overrides per entity and reuse from REST.
- Policies and hooks:
  - Apply `CanRead/CanWrite/CanRemove` in resolvers; map failures to GraphQL error `extensions.code`.
  - Always route list queries through `HookRunner` to enforce defaults and ceilings (ADR-0032).
- Dataloaders/perf:
  - Use `HotChocolate.DataLoader` (GreenDonut) to batch id lookups and relations, backed by repository bulk APIs.
- Limits/safety:
  - Configure depth, complexity, and timeouts on `IRequestExecutor`. Enforce page size ceilings via `QueryOptions`/`SoraDataBehavior` defaults.
- Dev tooling:
  - Optionally serve Banana Cake Pop in Development only behind a flag. Production exposes only the controller route.
- Coexistence with REST:
  - REST remains under `/api/*`; GraphQL is `/graphql`. They share repositories, hooks, and policies; conventions differ (pagination headers vs payload; no transformers in GraphQL).

## Implementation plan

Phase 0: Module scaffold and bootstrapping
- Project `Sora.Web.GraphQl` with folders: `Controllers`, `Types`, `Inputs`, `Resolvers`, `Dataloaders`, `Options`, `Infrastructure`.
- `Infrastructure/Constants.cs` (route "/graphql", default limits).
- `ServiceCollectionExtensions.AddSoraGraphQl()`:
  - Scan for `IEntity<>` types (reuse Data.Core discovery pattern).
  - Register Hot Chocolate `IRequestExecutor` with dynamic type descriptors; register dataloaders and display provider.
  - Do not map inline endpoints.
- `Controllers/GraphQlController` (POST `/graphql`) executes requests via `IRequestExecutor`.
- Tests: DI wiring and controller smoke test.

Phase 1: Queries (read path)
- `ObjectType<TEntity>` generator including `display` field.
- Connection types and `pageInfo`.
- Query fields: singular by id; plural with `filter/sort/page/size/set`.
- Typed filter/sort inputs (subset): `equals`, `contains` (with `ignoreCase`), `gt/gte/lt/lte`, `in`.
- Build `QueryOptions` from inputs; run `HookRunner`.
- Tests: filter/sort → `QueryOptions` mapping; pagination and `totalCount`; `CanRead` guard; complexity limits.

Phase 2: Mutations (write path)
- `<Entity>Input` from public writable props.
- Mutations: upsert one/many; delete one/many using `RepositoryFacade/IBatchSet`.
- Guards: `CanWrite/CanRemove`; map to GraphQL error extensions.
- Tests: upsert round-trip; batch ops; guard failures; id generation parity.

Phase 3: Dataloaders and polish
- Id-based dataloaders; relation loaders as needed.
- Depth/complexity/timeouts; enforce page size ceiling.
- Dev-only IDE toggle; docs and SDL export.

## Acceptance criteria
- `AddSoraGraphQl` exposes schema for all discovered `IEntity<>` types without manual wiring.
- Queries/mutations use the same repositories and respect hooks/policies.
- Typed filters/sorts correctly translate to `QueryOptions` (unit-tested).
- `GraphQlController` is the only public endpoint for GraphQL.
- Coexists with REST samples without route conflicts.

## Consequences

- Positive
  - Seamless GraphQL with minimal per-entity code; shared behavior via `QueryOptions`/hooks.
  - Strongly-typed filters/sorts and `display` improve DX.
  - Controller-first hosting matches Sora guidelines.
- Negative
  - Pagination metadata in payload (not headers); expected for GraphQL.
  - Transformers/content negotiation don’t apply; shapes must be explicit.
  - Adds a dependency on Hot Chocolate.
- Risks
  - Filter generator must match ADR-0029/0031 semantics; mitigate with mapping tests.
  - Heavy queries if fields are over-exposed; mitigate with complexity limits and selective exposure.

## References
- ADR-0029: JSON filter language and endpoint
- ADR-0031: Filter ignore-case option
- ADR-0032: Paging pushdown and in-memory fallback
- ADR-0035: EntityController transformers (contrast with GraphQL)
- ADR-0040: Config and constants naming

## Out of scope (now)
- PATCH/JsonPatch; use typed updates instead.
- Subscriptions; consider later with messaging.
- Transformers in GraphQL; use explicit fields/types.

## Follow-ups
- Add a sample enabling both `EntityController<Todo>` and GraphQL for `Todo`.
- Docs for filter input syntax and examples per adapter.
